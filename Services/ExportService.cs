using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySQLManager.Models;

namespace MySQLManager.Services
{
    public class ExportService
    {
        private readonly ConnectionService _conn;

        public ExportService(ConnectionService conn) => _conn = conn;

        // ── CSV ───────────────────────────────────────────────

        public Task ExportCsvAsync(DataTable data, string filePath, bool includeHeader = true)
        {
            return Task.Run(() =>
            {
                using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

                if (includeHeader)
                {
                    var headers = data.Columns.Cast<DataColumn>()
                                      .Select(c => EscapeCsv(c.ColumnName));
                    sw.WriteLine(string.Join(",", headers));
                }

                foreach (DataRow row in data.Rows)
                {
                    var cols = row.ItemArray.Select(v =>
                        v == null || v == DBNull.Value ? "" : EscapeCsv(v.ToString()!));
                    sw.WriteLine(string.Join(",", cols));
                }
            });
        }

        private static string EscapeCsv(string val)
        {
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n'))
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            return val;
        }

        // ── SQL Dump (資料表) ─────────────────────────────────

        public async Task ExportTableSqlAsync(string database, string table, string filePath,
            bool includeCreate = true, bool includeData = true)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

            await sw.WriteLineAsync("-- MySQL Manager SQL Dump");
            await sw.WriteLineAsync("-- Database: " + database + "  Table: " + table);
            await sw.WriteLineAsync("-- Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await sw.WriteLineAsync();

            if (includeCreate)
            {
                var createSql = await _conn.GetCreateTableSqlAsync(database, table);
                await sw.WriteLineAsync("DROP TABLE IF EXISTS `" + table + "`;");
                await sw.WriteLineAsync(createSql + ";");
                await sw.WriteLineAsync();
            }

            if (includeData)
            {
                var result = await _conn.ExecuteQueryAsync(
                    "SELECT * FROM `" + database + "`.`" + table + "`;");

                if (result.Success && result.Data != null && result.Data.Rows.Count > 0)
                {
                    var dt = result.Data;
                    var colNames = string.Join(", ",
                        dt.Columns.Cast<DataColumn>().Select(c => "`" + c.ColumnName + "`"));

                    await sw.WriteLineAsync("-- Data for table `" + table + "`");
                    await sw.WriteLineAsync("INSERT INTO `" + table + "` (" + colNames + ") VALUES");

                    var rows = dt.Rows.Cast<DataRow>().ToList();
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var vals = rows[i].ItemArray.Select(v =>
                            v == null || v == DBNull.Value ? "NULL"
                            : IsNumeric(v) ? v.ToString()!
                                           : "'" + v.ToString()!.Replace("'", "''") + "'");
                        var line = "  (" + string.Join(", ", vals) + ")";
                        await sw.WriteLineAsync(i < rows.Count - 1 ? line + "," : line + ";");
                    }
                    await sw.WriteLineAsync();
                }
            }
        }

        // ── SQL Dump (整個資料庫) ─────────────────────────────

        public async Task ExportDatabaseSqlAsync(string database, string filePath,
            IProgress<(int current, int total, string table)>? progress = null)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

            await sw.WriteLineAsync("-- MySQL Manager SQL Dump");
            await sw.WriteLineAsync("-- Database: " + database);
            await sw.WriteLineAsync("-- Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await sw.WriteLineAsync();
            await sw.WriteLineAsync("CREATE DATABASE IF NOT EXISTS `" + database + "`;");
            await sw.WriteLineAsync("USE `" + database + "`;");
            await sw.WriteLineAsync();

            var tables = await _conn.GetTablesAsync(database);

            for (int i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                progress?.Report((i + 1, tables.Count, table));

                var createSql = await _conn.GetCreateTableSqlAsync(database, table);
                await sw.WriteLineAsync("-- Table: " + table);
                await sw.WriteLineAsync("DROP TABLE IF EXISTS `" + table + "`;");
                await sw.WriteLineAsync(createSql + ";");
                await sw.WriteLineAsync();

                var result = await _conn.ExecuteQueryAsync("SELECT * FROM `" + database + "`.`" + table + "`;");
                if (result.Success && result.Data?.Rows.Count > 0)
                {
                    var dt = result.Data!;
                    var colNames = string.Join(", ",
                        dt.Columns.Cast<DataColumn>().Select(c => "`" + c.ColumnName + "`"));

                    await sw.WriteLineAsync("INSERT INTO `" + table + "` (" + colNames + ") VALUES");
                    var rows = dt.Rows.Cast<DataRow>().ToList();
                    for (int r = 0; r < rows.Count; r++)
                    {
                        var vals = rows[r].ItemArray.Select(v =>
                            v == null || v == DBNull.Value ? "NULL"
                            : IsNumeric(v) ? v.ToString()!
                                           : "'" + v.ToString()!.Replace("'", "''") + "'");
                        var line = "  (" + string.Join(", ", vals) + ")";
                        await sw.WriteLineAsync(r < rows.Count - 1 ? line + "," : line + ";");
                    }
                    await sw.WriteLineAsync();
                }
            }
        }

        private static bool IsNumeric(object v) =>
            v is int or long or short or byte or float or double or decimal;

        // ── Excel (.xlsx) ─────────────────────────────────────

        /// <summary>匯出 DataTable 到 Excel 2007+ (.xlsx)，純 OpenXML，無外部套件</summary>
        public static async Task ExportXlsxAsync(DataTable table, string path)
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

                // _rels/.rels
                WriteZipEntry(archive, "_rels/.rels", BuildRootRels());

                // [Content_Types].xml
                WriteZipEntry(archive, "[Content_Types].xml", BuildContentTypes());

                // xl/_rels/workbook.xml.rels
                WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels());

                // xl/workbook.xml
                WriteZipEntry(archive, "xl/workbook.xml", BuildWorkbook());

                // xl/styles.xml
                WriteZipEntry(archive, "xl/styles.xml", BuildStyles());

                // xl/worksheets/sheet1.xml
                WriteZipEntry(archive, "xl/worksheets/sheet1.xml", BuildSheet(table));
            });
        }

        private static string BuildRootRels()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            sb.Append("<Relationship Id=\"rId1\"");
            sb.Append(" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\"");
            sb.Append(" Target=\"xl/workbook.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildContentTypes()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildWorkbookRels()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            sb.Append("<Relationship Id=\"rId1\"");
            sb.Append(" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"");
            sb.Append(" Target=\"worksheets/sheet1.xml\"/>");
            sb.Append("<Relationship Id=\"rId2\"");
            sb.Append(" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\"");
            sb.Append(" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildWorkbook()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"");
            sb.Append(" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        private static string BuildStyles()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<fonts>");
            sb.Append("<font><sz val=\"11\"/><name val=\"Calibri\"/></font>");
            sb.Append("<font><sz val=\"11\"/><b/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>");
            sb.Append("</fonts>");
            sb.Append("<fills>");
            sb.Append("<fill><patternFill patternType=\"none\"/></fill>");
            sb.Append("<fill><patternFill patternType=\"gray125\"/></fill>");
            sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1565C0\"/></patternFill></fill>");
            sb.Append("</fills>");
            sb.Append("<borders><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
            sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            sb.Append("<cellXfs>");
            sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>");
            sb.Append("<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\"><alignment horizontal=\"center\"/></xf>");
            sb.Append("</cellXfs>");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }

        private static string BuildSheet(DataTable table)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("</sheetView></sheetViews>");
            sb.Append("<sheetData>");

            // Header row (style index 1 = bold blue)
            sb.Append("<row r=\"1\">");
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var col = ColLetter(c + 1) + "1";
                var txt = XmlEscape(table.Columns[c].ColumnName);
                sb.Append("<c r=\"" + col + "\" s=\"1\" t=\"inlineStr\"><is><t>" + txt + "</t></is></c>");
            }
            sb.Append("</row>");

            // Data rows
            for (int r = 0; r < table.Rows.Count; r++)
            {
                int rowNum = r + 2;
                sb.Append("<row r=\"" + rowNum + "\">");
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var cellRef = ColLetter(c + 1) + rowNum;
                    var raw     = table.Rows[r][c];
                    var val     = raw == null || raw == DBNull.Value ? "" : raw.ToString()!;

                    if (double.TryParse(val, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                    {
                        sb.Append("<c r=\"" + cellRef + "\"><v>" + num.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</v></c>");
                    }
                    else
                    {
                        sb.Append("<c r=\"" + cellRef + "\" t=\"inlineStr\"><is><t>" + XmlEscape(val) + "</t></is></c>");
                    }
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData>");

            // AutoFilter
            var lastCol = ColLetter(table.Columns.Count);
            sb.Append("<autoFilter ref=\"A1:" + lastCol + "1\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
        {
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string ColLetter(int col)
        {
            var result = "";
            while (col > 0)
            {
                col--;
                result = (char)('A' + col % 26) + result;
                col /= 26;
            }
            return result;
        }

        private static string XmlEscape(string s)
        {
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }
    }
}
