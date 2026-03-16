using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MySQLManager.Models;

namespace MySQLManager.Services;

public enum CrudLanguage { CSharp, Python, PHP, TypeScript }

public static class CrudGeneratorService
{
    public static string Generate(
        string database, string tableName,
        List<ColumnInfo> columns, CrudLanguage lang)
    {
        return lang switch
        {
            CrudLanguage.CSharp     => GenerateCSharp(database, tableName, columns),
            CrudLanguage.Python     => GeneratePython(database, tableName, columns),
            CrudLanguage.PHP        => GeneratePhp(database, tableName, columns),
            CrudLanguage.TypeScript => GenerateTypeScript(database, tableName, columns),
            _                       => ""
        };
    }

    // ── C# (Dapper) ───────────────────────────────────────────

    private static string GenerateCSharp(string db, string table, List<ColumnInfo> cols)
    {
        var className  = ToPascalCase(table);
        var pkCol      = cols.FirstOrDefault(c => c.Key == "PRI") ?? cols.First();
        var pkProp     = ToPascalCase(pkCol.Field);
        var pkType     = MapToCSharpType(pkCol.Type);
        var nonPkCols  = cols.Where(c => c.Key != "PRI" && !c.Extra.Contains("auto_increment")).ToList();
        var allProps   = cols.Select(c => $"    public {MapToCSharpType(c.Type)} {ToPascalCase(c.Field)} {{ get; set; }}");
        var insertCols = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}`"));
        var insertVals = string.Join(", ", nonPkCols.Select(c => $"@{ToPascalCase(c.Field)}"));
        var updateSets = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}` = @{ToPascalCase(c.Field)}"));

        var sb = new StringBuilder();
        sb.AppendLine($"// Generated CRUD for `{db}`.`{table}`");
        sb.AppendLine("// Requires: Dapper, MySqlConnector");
        sb.AppendLine();
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using MySqlConnector;");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");
        foreach (var p in allProps) sb.AppendLine(p);
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public class {className}Repository");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly string _connStr;");
        sb.AppendLine($"    public {className}Repository(string connStr) => _connStr = connStr;");
        sb.AppendLine();
        sb.AppendLine($"    public async Task<IEnumerable<{className}>> GetAllAsync(int limit = 1000)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = new MySqlConnection(_connStr);");
        sb.AppendLine($"        return await conn.QueryAsync<{className}>(");
        sb.AppendLine($"            \"SELECT * FROM `{table}` LIMIT @limit\", new {{ limit }});");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public async Task<{className}?> GetByIdAsync({pkType} {ToCamelCase(pkCol.Field)})");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = new MySqlConnection(_connStr);");
        sb.AppendLine($"        return await conn.QueryFirstOrDefaultAsync<{className}>(");
        sb.AppendLine($"            \"SELECT * FROM `{table}` WHERE `{pkCol.Field}` = @id\",");
        sb.AppendLine($"            new {{ id = {ToCamelCase(pkCol.Field)} }});");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public async Task<int> InsertAsync({className} entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = new MySqlConnection(_connStr);");
        sb.AppendLine($"        return await conn.ExecuteAsync(");
        sb.AppendLine($"            \"INSERT INTO `{table}` ({insertCols}) VALUES ({insertVals})\",");
        sb.AppendLine("            entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public async Task<int> UpdateAsync({className} entity)");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = new MySqlConnection(_connStr);");
        sb.AppendLine($"        return await conn.ExecuteAsync(");
        sb.AppendLine($"            \"UPDATE `{table}` SET {updateSets} WHERE `{pkCol.Field}` = @{pkProp}\",");
        sb.AppendLine("            entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public async Task<int> DeleteAsync({pkType} {ToCamelCase(pkCol.Field)})");
        sb.AppendLine("    {");
        sb.AppendLine("        using var conn = new MySqlConnection(_connStr);");
        sb.AppendLine($"        return await conn.ExecuteAsync(");
        sb.AppendLine($"            \"DELETE FROM `{table}` WHERE `{pkCol.Field}` = @id\",");
        sb.AppendLine($"            new {{ id = {ToCamelCase(pkCol.Field)} }});");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Python (mysql-connector-python) ───────────────────────

    private static string GeneratePython(string db, string table, List<ColumnInfo> cols)
    {
        var pkCol     = cols.FirstOrDefault(c => c.Key == "PRI") ?? cols.First();
        var nonPkCols = cols.Where(c => c.Key != "PRI" && !c.Extra.Contains("auto_increment")).ToList();
        var allFields = string.Join(", ", cols.Select(c => $"'{c.Field}'"));
        var insertCols = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}`"));
        var insertPhs  = string.Join(", ", nonPkCols.Select(_ => "%s"));
        var updateSets = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}` = %s"));
        var className  = ToPascalCase(table);

        var sb = new StringBuilder();
        sb.AppendLine($"# Generated CRUD for `{db}`.`{table}`");
        sb.AppendLine("# Requires: mysql-connector-python");
        sb.AppendLine();
        sb.AppendLine("import mysql.connector");
        sb.AppendLine("from dataclasses import dataclass");
        sb.AppendLine("from typing import Optional, List");
        sb.AppendLine();
        sb.AppendLine("@dataclass");
        sb.AppendLine($"class {className}:");
        foreach (var col in cols)
            sb.AppendLine($"    {col.Field}: {MapToPythonType(col.Type)} = None");
        sb.AppendLine();
        sb.AppendLine($"class {className}Repository:");
        sb.AppendLine($"    def __init__(self, host, user, password, database='{db}'):");
        sb.AppendLine("        self.config = dict(host=host, user=user, password=password, database=database)");
        sb.AppendLine();
        sb.AppendLine($"    def get_all(self, limit=1000) -> List[{className}]:");
        sb.AppendLine("        with mysql.connector.connect(**self.config) as conn:");
        sb.AppendLine("            cursor = conn.cursor(dictionary=True)");
        sb.AppendLine($"            cursor.execute(f'SELECT * FROM `{table}` LIMIT %s', (limit,))");
        sb.AppendLine($"            return [{className}(**row) for row in cursor.fetchall()]");
        sb.AppendLine();
        sb.AppendLine($"    def get_by_id(self, id) -> Optional[{className}]:");
        sb.AppendLine("        with mysql.connector.connect(**self.config) as conn:");
        sb.AppendLine("            cursor = conn.cursor(dictionary=True)");
        sb.AppendLine($"            cursor.execute('SELECT * FROM `{table}` WHERE `{pkCol.Field}` = %s', (id,))");
        sb.AppendLine("            row = cursor.fetchone()");
        sb.AppendLine($"            return {className}(**row) if row else None");
        sb.AppendLine();
        sb.AppendLine($"    def insert(self, entity: {className}) -> int:");
        sb.AppendLine("        with mysql.connector.connect(**self.config) as conn:");
        sb.AppendLine("            cursor = conn.cursor()");
        sb.AppendLine($"            cursor.execute(");
        sb.AppendLine($"                'INSERT INTO `{table}` ({insertCols}) VALUES ({insertPhs})',");
        sb.AppendLine($"                ({string.Join(", ", nonPkCols.Select(c => $"entity.{c.Field}"))},)");
        sb.AppendLine("            )");
        sb.AppendLine("            conn.commit()");
        sb.AppendLine("            return cursor.lastrowid");
        sb.AppendLine();
        sb.AppendLine($"    def update(self, entity: {className}) -> int:");
        sb.AppendLine("        with mysql.connector.connect(**self.config) as conn:");
        sb.AppendLine("            cursor = conn.cursor()");
        sb.AppendLine($"            cursor.execute(");
        sb.AppendLine($"                'UPDATE `{table}` SET {updateSets} WHERE `{pkCol.Field}` = %s',");
        sb.AppendLine($"                ({string.Join(", ", nonPkCols.Select(c => $"entity.{c.Field}"))}, entity.{pkCol.Field})");
        sb.AppendLine("            )");
        sb.AppendLine("            conn.commit()");
        sb.AppendLine("            return cursor.rowcount");
        sb.AppendLine();
        sb.AppendLine($"    def delete(self, id) -> int:");
        sb.AppendLine("        with mysql.connector.connect(**self.config) as conn:");
        sb.AppendLine("            cursor = conn.cursor()");
        sb.AppendLine($"            cursor.execute('DELETE FROM `{table}` WHERE `{pkCol.Field}` = %s', (id,))");
        sb.AppendLine("            conn.commit()");
        sb.AppendLine("            return cursor.rowcount");
        return sb.ToString();
    }

    // ── PHP (PDO) ─────────────────────────────────────────────

    private static string GeneratePhp(string db, string table, List<ColumnInfo> cols)
    {
        var pkCol     = cols.FirstOrDefault(c => c.Key == "PRI") ?? cols.First();
        var nonPkCols = cols.Where(c => c.Key != "PRI" && !c.Extra.Contains("auto_increment")).ToList();
        var className = ToPascalCase(table);
        var insertCols = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}`"));
        var insertVals = string.Join(", ", nonPkCols.Select(c => $":{c.Field}"));
        var updateSets = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}` = :{c.Field}"));

        var sb = new StringBuilder();
        sb.AppendLine($"<?php");
        sb.AppendLine($"// Generated CRUD for `{db}`.`{table}`");
        sb.AppendLine();
        sb.AppendLine($"class {className} {{");
        foreach (var col in cols)
            sb.AppendLine($"    public ?{MapToPhpType(col.Type)} ${col.Field} = null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"class {className}Repository {{");
        sb.AppendLine("    private PDO $pdo;");
        sb.AppendLine();
        sb.AppendLine($"    public function __construct(string $dsn, string $user, string $pass) {{");
        sb.AppendLine("        $this->pdo = new PDO($dsn, $user, $pass, [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public function getAll(int $limit = 1000): array {{");
        sb.AppendLine($"        $stmt = $this->pdo->prepare('SELECT * FROM `{table}` LIMIT :limit');");
        sb.AppendLine("        $stmt->bindValue(':limit', $limit, PDO::PARAM_INT);");
        sb.AppendLine("        $stmt->execute();");
        sb.AppendLine($"        return $stmt->fetchAll(PDO::FETCH_CLASS, '{className}');");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public function getById(mixed $id): ?{className} {{");
        sb.AppendLine($"        $stmt = $this->pdo->prepare('SELECT * FROM `{table}` WHERE `{pkCol.Field}` = :id');");
        sb.AppendLine("        $stmt->execute([':id' => $id]);");
        sb.AppendLine($"        $result = $stmt->fetchObject('{className}');");
        sb.AppendLine("        return $result ?: null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public function insert({className} $entity): int {{");
        sb.AppendLine($"        $stmt = $this->pdo->prepare('INSERT INTO `{table}` ({insertCols}) VALUES ({insertVals})');");
        sb.AppendLine($"        $stmt->execute([{string.Join(", ", nonPkCols.Select(c => $"':{c.Field}' => $entity->{c.Field}"))}]);");
        sb.AppendLine("        return (int)$this->pdo->lastInsertId();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public function update({className} $entity): int {{");
        sb.AppendLine($"        $stmt = $this->pdo->prepare('UPDATE `{table}` SET {updateSets} WHERE `{pkCol.Field}` = :{pkCol.Field}');");
        sb.AppendLine($"        $stmt->execute([{string.Join(", ", cols.Where(c => c.Key != "PRI" || !c.Extra.Contains("auto_increment")).Select(c => $"':{c.Field}' => $entity->{c.Field}"))}]);");
        sb.AppendLine("        return $stmt->rowCount();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public function delete(mixed $id): int {{");
        sb.AppendLine($"        $stmt = $this->pdo->prepare('DELETE FROM `{table}` WHERE `{pkCol.Field}` = :id');");
        sb.AppendLine("        $stmt->execute([':id' => $id]);");
        sb.AppendLine("        return $stmt->rowCount();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── TypeScript (mysql2) ───────────────────────────────────

    private static string GenerateTypeScript(string db, string table, List<ColumnInfo> cols)
    {
        var pkCol     = cols.FirstOrDefault(c => c.Key == "PRI") ?? cols.First();
        var nonPkCols = cols.Where(c => c.Key != "PRI" && !c.Extra.Contains("auto_increment")).ToList();
        var className = ToPascalCase(table);
        var insertCols = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}`"));
        var insertPhs  = string.Join(", ", nonPkCols.Select(_ => "?"));
        var updateSets = string.Join(", ", nonPkCols.Select(c => $"`{c.Field}` = ?"));

        var sb = new StringBuilder();
        sb.AppendLine($"// Generated CRUD for `{db}`.`{table}`");
        sb.AppendLine("// Requires: mysql2/promise");
        sb.AppendLine();
        sb.AppendLine("import mysql from 'mysql2/promise';");
        sb.AppendLine();
        sb.AppendLine($"export interface {className} {{");
        foreach (var col in cols)
            sb.AppendLine($"  {col.Field}{(col.Null == "YES" ? "?" : "")}: {MapToTsType(col.Type)};");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"export class {className}Repository {{");
        sb.AppendLine("  constructor(private pool: mysql.Pool) {}");
        sb.AppendLine();
        sb.AppendLine($"  async getAll(limit = 1000): Promise<{className}[]> {{");
        sb.AppendLine($"    const [rows] = await this.pool.execute('SELECT * FROM `{table}` LIMIT ?', [limit]);");
        sb.AppendLine($"    return rows as {className}[];");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine($"  async getById(id: {MapToTsType(pkCol.Type)}): Promise<{className} | null> {{");
        sb.AppendLine($"    const [rows] = await this.pool.execute('SELECT * FROM `{table}` WHERE `{pkCol.Field}` = ?', [id]);");
        sb.AppendLine($"    const arr = rows as {className}[];");
        sb.AppendLine("    return arr[0] ?? null;");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine($"  async insert(entity: Omit<{className}, '{pkCol.Field}'>): Promise<number> {{");
        sb.AppendLine($"    const [result] = await this.pool.execute(");
        sb.AppendLine($"      'INSERT INTO `{table}` ({insertCols}) VALUES ({insertPhs})',");
        sb.AppendLine($"      [{string.Join(", ", nonPkCols.Select(c => $"entity.{c.Field}"))}]");
        sb.AppendLine("    );");
        sb.AppendLine("    return (result as mysql.ResultSetHeader).insertId;");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine($"  async update(entity: {className}): Promise<number> {{");
        sb.AppendLine($"    const [result] = await this.pool.execute(");
        sb.AppendLine($"      'UPDATE `{table}` SET {updateSets} WHERE `{pkCol.Field}` = ?',");
        sb.AppendLine($"      [{string.Join(", ", nonPkCols.Select(c => $"entity.{c.Field}"))}, entity.{pkCol.Field}]");
        sb.AppendLine("    );");
        sb.AppendLine("    return (result as mysql.ResultSetHeader).affectedRows;");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine($"  async delete(id: {MapToTsType(pkCol.Type)}): Promise<number> {{");
        sb.AppendLine($"    const [result] = await this.pool.execute('DELETE FROM `{table}` WHERE `{pkCol.Field}` = ?', [id]);");
        sb.AppendLine("    return (result as mysql.ResultSetHeader).affectedRows;");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Type Mapping Helpers ──────────────────────────────────

    private static string MapToCSharpType(string mysqlType)
    {
        var t = mysqlType.ToLowerInvariant().Split('(')[0].Trim().Replace(" unsigned", "");
        return t switch
        {
            "int" or "mediumint" => "int",
            "bigint"             => "long",
            "smallint"           => "short",
            "tinyint"            => "byte",
            "decimal" or "numeric" or "float" or "double" => "decimal",
            "varchar" or "char" or "text" or "longtext"
                or "mediumtext" or "tinytext" or "enum" or "set" => "string?",
            "datetime" or "timestamp"    => "DateTime",
            "date"                       => "DateOnly",
            "time"                       => "TimeOnly",
            "boolean" or "bit"           => "bool",
            "json"                       => "string?",
            _                            => "object?"
        };
    }

    private static string MapToPythonType(string mysqlType)
    {
        var t = mysqlType.ToLowerInvariant().Split('(')[0].Trim();
        return t switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "mediumint" => "int",
            "decimal" or "float" or "double" => "float",
            "boolean" or "bit"   => "bool",
            "datetime" or "timestamp" or "date" => "str",
            _                    => "str"
        };
    }

    private static string MapToPhpType(string mysqlType)
    {
        var t = mysqlType.ToLowerInvariant().Split('(')[0].Trim();
        return t switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "mediumint" => "int",
            "decimal" or "float" or "double" => "float",
            "boolean" or "bit"   => "bool",
            _                    => "string"
        };
    }

    private static string MapToTsType(string mysqlType)
    {
        var t = mysqlType.ToLowerInvariant().Split('(')[0].Trim();
        return t switch
        {
            "int" or "bigint" or "smallint" or "tinyint" or "mediumint"
                or "decimal" or "float" or "double" => "number",
            "boolean" or "bit" => "boolean",
            _                  => "string"
        };
    }

    private static string ToPascalCase(string s) =>
        string.Concat(s.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));

    private static string ToCamelCase(string s)
    {
        var pascal = ToPascalCase(s);
        return char.ToLower(pascal[0]) + pascal[1..];
    }
}
