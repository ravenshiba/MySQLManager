-- ============================================================
-- MySQL Manager 測試資料庫（MySqlConnector 相容版本）
-- 不含 DELIMITER，可直接在 MySQL Manager 執行
-- ============================================================

SET NAMES utf8mb4;
SET time_zone = '+08:00';
SET foreign_key_checks = 0;

DROP DATABASE IF EXISTS mysql_manager_test;
CREATE DATABASE mysql_manager_test
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE mysql_manager_test;

-- ============================================================
-- 1. 類別表
-- ============================================================
CREATE TABLE categories (
    id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    parent_id   INT UNSIGNED NULL,
    description TEXT,
    icon        VARCHAR(50),
    sort_order  INT DEFAULT 0,
    is_active   TINYINT(1) DEFAULT 1,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (parent_id) REFERENCES categories(id) ON DELETE SET NULL,
    INDEX idx_parent (parent_id),
    INDEX idx_active (is_active)
) ENGINE=InnoDB;

INSERT INTO categories (name, parent_id, description, icon, sort_order) VALUES
('電子產品', NULL, '所有電子相關商品', '💻', 1),
('服飾', NULL, '男女服飾及配件', '👕', 2),
('食品飲料', NULL, '生鮮食品與飲料', '🍎', 3),
('家居生活', NULL, '家具與生活用品', '🏠', 4),
('運動戶外', NULL, '運動器材與戶外用品', '⚽', 5),
('手機平板', 1, '智慧型手機與平板電腦', '📱', 1),
('筆記型電腦', 1, '各品牌筆電', '💻', 2),
('耳機音響', 1, '有線無線耳機及音響設備', '🎧', 3),
('相機攝影', 1, '數位相機與攝影器材', '📷', 4),
('男裝', 2, '男性服飾', '👔', 1),
('女裝', 2, '女性服飾', '👗', 2),
('鞋類', 2, '各類鞋子', '👟', 3),
('零食點心', 3, '餅乾糖果零食', '🍪', 1),
('飲料沖泡', 3, '茶飲咖啡果汁', '☕', 2),
('健身器材', 5, '重訓有氧器材', '🏋️', 1);

-- ============================================================
-- 2. 供應商
-- ============================================================
CREATE TABLE suppliers (
    id           INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    company_name VARCHAR(200) NOT NULL,
    contact_name VARCHAR(100),
    email        VARCHAR(150) UNIQUE,
    phone        VARCHAR(20),
    address      VARCHAR(300),
    city         VARCHAR(50),
    country      VARCHAR(50) DEFAULT '台灣',
    rating       DECIMAL(3,1) DEFAULT 5.0,
    is_active    TINYINT(1) DEFAULT 1,
    notes        TEXT,
    created_at   DATETIME DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;

INSERT INTO suppliers (company_name, contact_name, email, phone, address, city, rating) VALUES
('台灣科技供應有限公司', '陳志明', 'chen@tw-tech.com', '02-2345-6789', '台北市信義區松高路100號', '台北', 4.8),
('全球電子貿易股份', '林美華', 'lin@global-elec.com', '02-8765-4321', '新北市板橋區文化路200號', '新北', 4.5),
('優質服飾供應商', '張小芬', 'chang@fashion.tw', '04-2233-4455', '台中市西區台灣大道50號', '台中', 4.7),
('新鮮食品直送', '王大中', 'wang@fresh-food.tw', '07-3344-5566', '高雄市鳳山區中山路300號', '高雄', 4.9),
('運動用品專業代理', '李建國', 'lee@sport-pro.tw', '03-5566-7788', '桃園市中壢區中央路150號', '桃園', 4.6),
('Apple 台灣授權商', '蘇文德', 'su@apple-tw.com', '02-2777-8888', '台北市大安區忠孝東路400號', '台北', 5.0),
('Samsung 官方代理', '鄭雅玲', 'cheng@samsung-tw.com', '02-2888-9999', '台北市松山區敦化南路100號', '台北', 4.9),
('日本進口食品代理', '吳志豪', 'wu@japan-food.tw', '02-2666-7777', '台北市中山區林森北路200號', '台北', 4.7);

-- ============================================================
-- 3. 商品表
-- ============================================================
CREATE TABLE products (
    id            INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    sku           VARCHAR(50) UNIQUE NOT NULL,
    name          VARCHAR(300) NOT NULL,
    category_id   INT UNSIGNED,
    supplier_id   INT UNSIGNED,
    description   TEXT,
    price         DECIMAL(10,2) NOT NULL,
    cost          DECIMAL(10,2),
    stock_qty     INT DEFAULT 0,
    min_stock     INT DEFAULT 10,
    weight_kg     DECIMAL(6,3),
    is_active     TINYINT(1) DEFAULT 1,
    is_featured   TINYINT(1) DEFAULT 0,
    specs         JSON,
    created_at    DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at    DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES categories(id),
    FOREIGN KEY (supplier_id) REFERENCES suppliers(id),
    INDEX idx_category (category_id),
    INDEX idx_supplier (supplier_id),
    INDEX idx_price (price),
    INDEX idx_stock (stock_qty),
    INDEX idx_active_featured (is_active, is_featured),
    FULLTEXT INDEX ft_name_desc (name, description)
) ENGINE=InnoDB;

INSERT INTO products (sku, name, category_id, supplier_id, price, cost, stock_qty, min_stock, weight_kg, is_featured, specs) VALUES
('IP15PM-256-BLK', 'iPhone 15 Pro Max 256GB 黑色', 6, 6, 52900, 42000, 45, 10, 0.221, 1, '{"display":"6.7吋","chip":"A17 Pro","camera":"48MP","battery":"4422mAh"}'),
('IP15PM-512-WHT', 'iPhone 15 Pro Max 512GB 白色', 6, 6, 62900, 50000, 28, 10, 0.221, 1, '{"display":"6.7吋","chip":"A17 Pro","camera":"48MP","battery":"4422mAh"}'),
('SAM-S24U-256',   'Samsung Galaxy S24 Ultra 256GB', 6, 7, 44900, 35000, 62, 15, 0.232, 1, '{"display":"6.8吋","chip":"Snapdragon 8 Gen3","camera":"200MP","battery":"5000mAh"}'),
('MBP-M3-14-512',  'MacBook Pro 14吋 M3 512GB', 7, 6, 69900, 55000, 18, 5, 1.550, 1, '{"display":"14.2吋","chip":"Apple M3","ram":"18GB","storage":"512GB SSD"}'),
('SONY-WH1000XM5', 'Sony WH-1000XM5 降噪耳機', 8, 1, 10900, 7500, 95, 20, 0.250, 1, '{"type":"頭戴式","connection":"藍牙5.2","anc":true,"battery":"30hr"}'),
('APPLE-AIRPODS-PRO2', 'AirPods Pro 第二代', 8, 6, 7490, 5500, 120, 30, 0.061, 1, '{"type":"入耳式","connection":"藍牙5.3","anc":true,"battery":"6hr"}'),
('FUJI-X100VI',    '富士 X100VI 數位相機', 9, 2, 57900, 45000, 12, 5, 0.521, 1, '{"sensor":"4020萬像素","lens":"23mm F2","video":"6.2K"}'),
('POLO-M-SLIM-BLU','POLO 修身男衫 藍色 M', 10, 3, 890, 350, 200, 50, 0.280, 0, '{"material":"100% 棉","size":"M","color":"藍色"}'),
('POLO-L-SLIM-WHT','POLO 修身男衫 白色 L', 10, 3, 890, 350, 185, 50, 0.280, 0, '{"material":"100% 棉","size":"L","color":"白色"}'),
('DRESS-FLORAL-S', '碎花洋裝 S號', 11, 3, 1290, 480, 88, 20, 0.350, 1, '{"material":"雪紡","size":"S","color":"碎花"}'),
('NIKE-AIR-MAX-270','Nike Air Max 270 男款 US10', 12, 5, 3600, 2100, 42, 10, 0.590, 1, '{"brand":"Nike","model":"Air Max 270","size":"US10"}'),
('LAYS-ORIGINAL-120','樂事原味洋芋片 120g', 13, 4, 45, 22, 500, 100, 0.130, 0, '{"weight":"120g","flavor":"原味"}'),
('NESCAFE-GOLD-200','雀巢金牌咖啡 200g', 14, 8, 299, 150, 320, 80, 0.220, 0, '{"weight":"200g","type":"即溶咖啡","origin":"瑞士"}'),
('JAPAN-MATCHA-100','日本宇治抹茶粉 100g', 14, 8, 450, 220, 180, 40, 0.120, 1, '{"weight":"100g","origin":"日本宇治","grade":"儀式級"}'),
('DUMBBELL-20KG',  '可調式啞鈴組 20kg 一對', 15, 5, 3200, 1800, 35, 8, 20.000, 0, '{"weight":"20kg×2","material":"鑄鐵","adjustable":true}');

-- ============================================================
-- 4. 會員表
-- ============================================================
CREATE TABLE members (
    id             INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    username       VARCHAR(50) UNIQUE NOT NULL,
    email          VARCHAR(150) UNIQUE NOT NULL,
    password_hash  VARCHAR(255) NOT NULL,
    full_name      VARCHAR(100),
    phone          VARCHAR(20),
    birthday       DATE,
    gender         ENUM('M','F','other') DEFAULT 'other',
    points         INT DEFAULT 0,
    level          TINYINT DEFAULT 1 COMMENT '1=一般 2=銀卡 3=金卡 4=白金',
    is_active      TINYINT(1) DEFAULT 1,
    email_verified TINYINT(1) DEFAULT 0,
    last_login     DATETIME,
    created_at     DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_email (email),
    INDEX idx_level (level),
    INDEX idx_points (points)
) ENGINE=InnoDB;

INSERT INTO members (username, email, password_hash, full_name, phone, birthday, gender, points, level, email_verified, last_login) VALUES
('alice_wang',  'alice@example.com',  SHA2('pass123',256), '王小美', '0912-345-678', '1990-05-15', 'F', 15200, 3, 1, NOW() - INTERVAL 2  HOUR),
('bob_chen',    'bob@example.com',    SHA2('pass456',256), '陳大強', '0923-456-789', '1985-08-22', 'M',  8500, 2, 1, NOW() - INTERVAL 1  DAY),
('carol_lin',   'carol@example.com',  SHA2('pass789',256), '林嘉玲', '0934-567-890', '1992-03-10', 'F', 32000, 4, 1, NOW() - INTERVAL 3  HOUR),
('david_wu',    'david@example.com',  SHA2('passabc',256), '吳大偉', '0945-678-901', '1988-11-30', 'M',  4200, 2, 1, NOW() - INTERVAL 2  DAY),
('eva_huang',   'eva@example.com',    SHA2('passdef',256), '黃依婷', '0956-789-012', '1995-07-04', 'F',   980, 1, 1, NOW() - INTERVAL 5  DAY),
('frank_lee',   'frank@example.com',  SHA2('passghi',256), '李志遠', '0967-890-123', '1983-12-25', 'M', 21500, 4, 1, NOW() - INTERVAL 1  HOUR),
('grace_chiu',  'grace@example.com',  SHA2('passjkl',256), '邱雅慧', '0978-901-234', '1991-09-18', 'F',  6800, 2, 1, NOW() - INTERVAL 4  HOUR),
('henry_chang', 'henry@example.com',  SHA2('passmno',256), '張志豪', '0989-012-345', '1987-02-14', 'M', 12100, 3, 1, NOW() - INTERVAL 6  HOUR),
('iris_su',     'iris@example.com',   SHA2('passpqr',256), '蘇雅琪', '0901-123-456', '1993-06-28', 'F',  3300, 1, 0, NOW() - INTERVAL 10 DAY),
('jason_liao',  'jason@example.com',  SHA2('passstu',256), '廖建宏', '0912-234-567', '1986-04-07', 'M', 18700, 3, 1, NOW() - INTERVAL 30 MINUTE),
('kelly_tsai',  'kelly@example.com',  SHA2('passvwx',256), '蔡佩君', '0923-345-678', '1994-10-12', 'F',  7600, 2, 1, NOW() - INTERVAL 3  DAY),
('louis_hsu',   'louis@example.com',  SHA2('passyza',256), '許家豪', '0934-456-789', '1989-01-19', 'M',  2100, 1, 1, NOW() - INTERVAL 7  DAY),
('mary_kuo',    'mary@example.com',   SHA2('passbcd',256), '郭美玲', '0945-567-890', '1996-08-03', 'F', 44500, 4, 1, NOW() - INTERVAL 15 MINUTE),
('nick_yang',   'nick@example.com',   SHA2('passedf',256), '楊志明', '0956-678-901', '1984-05-21', 'M',  9200, 2, 1, NOW() - INTERVAL 2  HOUR),
('olivia_pan',  'olivia@example.com', SHA2('passghi',256), '潘雅文', '0967-789-012', '1997-11-08', 'F',  1500, 1, 0, NOW() - INTERVAL 20 DAY);

-- ============================================================
-- 5. 地址表
-- ============================================================
CREATE TABLE addresses (
    id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    member_id   INT UNSIGNED NOT NULL,
    label       VARCHAR(50) DEFAULT '家',
    recipient   VARCHAR(100) NOT NULL,
    phone       VARCHAR(20),
    postal_code VARCHAR(10),
    city        VARCHAR(50),
    district    VARCHAR(50),
    address     VARCHAR(300) NOT NULL,
    is_default  TINYINT(1) DEFAULT 0,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (member_id) REFERENCES members(id) ON DELETE CASCADE,
    INDEX idx_member (member_id)
) ENGINE=InnoDB;

INSERT INTO addresses (member_id, label, recipient, phone, postal_code, city, district, address, is_default) VALUES
(1,  '家',   '王小美', '0912-345-678', '10699', '台北市', '大安區', '忠孝東路四段100號5樓', 1),
(1,  '公司', '王小美', '0912-345-678', '10488', '台北市', '中山區', '南京東路三段200號',    0),
(2,  '家',   '陳大強', '0923-456-789', '22041', '新北市', '板橋區', '文化路二段50號3樓',   1),
(3,  '家',   '林嘉玲', '0934-567-890', '40001', '台中市', '中區',   '台灣大道一段100號',   1),
(4,  '家',   '吳大偉', '0945-678-901', '30001', '新竹市', '東區',   '光復路一段300號',     1),
(5,  '家',   '黃依婷', '0956-789-012', '70001', '台南市', '東區',   '東門路一段150號',     1),
(6,  '家',   '李志遠', '0967-890-123', '80001', '高雄市', '三民區', '建國二路200號',       1),
(7,  '家',   '邱雅慧', '0978-901-234', '32001', '桃園市', '桃園區', '中正路250號',         1),
(8,  '家',   '張志豪', '0989-012-345', '43001', '台中市', '北區',   '北屯路100號',         1),
(9,  '家',   '蘇雅琪', '0901-123-456', '20001', '基隆市', '中正區', '義一路50號',          1),
(10, '家',   '廖建宏', '0912-234-567', '10001', '台北市', '中正區', '重慶南路一段88號',    1),
(13, '家',   '郭美玲', '0945-567-890', '10699', '台北市', '信義區', '松高路100號',         1),
(13, '公司', '郭美玲', '0945-567-890', '10489', '台北市', '中山區', '敦化北路150號',       0);

-- ============================================================
-- 6. 訂單主表
-- ============================================================
CREATE TABLE orders (
    id             INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    order_no       VARCHAR(30) UNIQUE NOT NULL,
    member_id      INT UNSIGNED NOT NULL,
    address_id     INT UNSIGNED,
    status         ENUM('pending','confirmed','processing','shipped','delivered','cancelled','refunded') DEFAULT 'pending',
    payment_method ENUM('credit_card','line_pay','atm','cod') DEFAULT 'credit_card',
    payment_status ENUM('unpaid','paid','refunded') DEFAULT 'unpaid',
    subtotal       DECIMAL(12,2) NOT NULL DEFAULT 0,
    discount_amt   DECIMAL(10,2) DEFAULT 0,
    shipping_fee   DECIMAL(8,2)  DEFAULT 60,
    total_amt      DECIMAL(12,2) NOT NULL DEFAULT 0,
    points_used    INT DEFAULT 0,
    points_earned  INT DEFAULT 0,
    coupon_code    VARCHAR(30),
    notes          TEXT,
    shipped_at     DATETIME,
    delivered_at   DATETIME,
    created_at     DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at     DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (member_id)  REFERENCES members(id),
    FOREIGN KEY (address_id) REFERENCES addresses(id),
    INDEX idx_member  (member_id),
    INDEX idx_status  (status),
    INDEX idx_created (created_at),
    INDEX idx_order_no(order_no)
) ENGINE=InnoDB;

INSERT INTO orders (order_no, member_id, address_id, status, payment_method, payment_status,
                    subtotal, discount_amt, shipping_fee, total_amt, points_used, points_earned, created_at)
SELECT
    CONCAT('ORD-', DATE_FORMAT(NOW() - INTERVAL n DAY, '%Y%m%d'), '-', LPAD(n, 4, '0')),
    1 + (n MOD 15),
    1 + (n MOD 13),
    ELT(1 + (n MOD 7), 'pending','confirmed','processing','shipped','delivered','delivered','cancelled'),
    ELT(1 + (n MOD 4), 'credit_card','line_pay','atm','cod'),
    ELT(1 + (n MOD 3), 'paid','paid','unpaid'),
    ROUND(500 + (n * 1234.56 MOD 50000), 2),
    ROUND((n MOD 5) * 100, 2),
    IF(n MOD 3 = 0, 0, 60),
    ROUND(500 + (n * 1234.56 MOD 50000) - (n MOD 5)*100 + IF(n MOD 3=0,0,60), 2),
    (n MOD 10) * 50,
    ROUND((500 + (n * 1234.56 MOD 50000)) / 100),
    NOW() - INTERVAL n DAY
FROM (
    SELECT a.N + b.N*10 + 1 AS n
    FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
          UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
         (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4 UNION SELECT 5) b
    WHERE a.N + b.N*10 < 60
) nums;

-- ============================================================
-- 7. 訂單明細
-- ============================================================
CREATE TABLE order_items (
    id           INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    order_id     INT UNSIGNED NOT NULL,
    product_id   INT UNSIGNED NOT NULL,
    product_name VARCHAR(300) NOT NULL,
    sku          VARCHAR(50),
    qty          INT NOT NULL DEFAULT 1,
    unit_price   DECIMAL(10,2) NOT NULL,
    discount     DECIMAL(10,2) DEFAULT 0,
    subtotal     DECIMAL(12,2) NOT NULL,
    created_at   DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (order_id)   REFERENCES orders(id)   ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(id),
    INDEX idx_order   (order_id),
    INDEX idx_product (product_id)
) ENGINE=InnoDB;

INSERT INTO order_items (order_id, product_id, product_name, sku, qty, unit_price, subtotal)
SELECT o.id, p.id, p.name, p.sku,
       1 + (o.id MOD 3),
       p.price,
       p.price * (1 + (o.id MOD 3))
FROM orders o
JOIN products p ON p.id = 1 + (o.id MOD 15);

INSERT INTO order_items (order_id, product_id, product_name, sku, qty, unit_price, subtotal)
SELECT o.id, p.id, p.name, p.sku, 1, p.price, p.price
FROM orders o
JOIN products p ON p.id = 1 + ((o.id + 7) MOD 15)
WHERE o.id MOD 2 = 0;

-- ============================================================
-- 8. 商品評論
-- ============================================================
CREATE TABLE reviews (
    id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    product_id  INT UNSIGNED NOT NULL,
    member_id   INT UNSIGNED NOT NULL,
    order_id    INT UNSIGNED,
    rating      TINYINT NOT NULL,
    title       VARCHAR(200),
    content     TEXT,
    is_verified TINYINT(1) DEFAULT 0,
    helpful_cnt INT DEFAULT 0,
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (product_id) REFERENCES products(id),
    FOREIGN KEY (member_id)  REFERENCES members(id),
    FOREIGN KEY (order_id)   REFERENCES orders(id),
    INDEX idx_product (product_id),
    INDEX idx_member  (member_id),
    INDEX idx_rating  (rating)
) ENGINE=InnoDB;

INSERT INTO reviews (product_id, member_id, rating, title, content, is_verified, helpful_cnt) VALUES
(1,  1,  5, '非常滿意的購物體驗', '手機品質很好，外觀漂亮，運送很快。鏡頭超強，拍照效果令人驚豔！', 1, 42),
(1,  3,  4, '整體不錯但偏貴',     '功能很強大，但售價確實有點高。相機和效能都很出色。',              1, 18),
(1,  6,  5, '值得入手的旗艦機',   '換機後非常滿意，Pro Max 的螢幕很大很清晰，鈦金屬邊框質感一流。', 1, 31),
(2,  2,  5, '三星旗艦真的強',     'S24 Ultra 的 S Pen 非常好用，AI 功能也很實用。',                 1, 25),
(3,  4,  4, '效能超強的筆電',     'M3 的效能真的很強，剪輯影片輕鬆流暢，電池續航可以撐一整天。',   1, 37),
(4,  5,  5, '降噪效果絕佳',       'WH-1000XM5 的降噪技術是目前市場最強的，戴著很舒適。',            1, 55),
(4,  7,  4, '好用但舒適度一般',   '降噪效果很好，但長時間配戴後耳朵有點悶熱。',                     1, 12),
(5,  1,  5, 'AirPods Pro 完美',   '搭配 iPhone 使用體驗超棒，空間音訊效果很棒。',                   1, 29),
(7,  3,  5, '富士色彩真的美',     'X100VI 的色彩直出就很漂亮，不需要後製。機身小巧攜帶方便。',      1, 48),
(13, 8,  5, '日本原裝抹茶粉',     '正宗宇治抹茶，味道濃郁不苦，泡出來的抹茶拿鐵很好喝！',           1, 22),
(10, 2,  3, 'Nike 球鞋品質一般',  '鞋型好看，但比想像中硬，需要一段磨合期。',                       1,  8),
(6,  9,  5, '最好的洋裝',         '布料很舒服，花紋漂亮，尺寸準確，值得五星！',                     1, 16),
(3,  10, 5, 'MacBook Pro 值得',   'M3 版本真的很強，剪 4K 影片完全不卡，電池超過 15 小時。',        1, 41),
(11, 13, 4, 'Polo 男裝品質好',    '棉質很舒服，版型修身，洗了幾次還是不會變形縮水。',               1, 14),
(14, 14, 5, '啞鈴做工紮實',       '可調式設計很方便，做工紮實，表面防滑，居家健身首選。',            1, 19);

-- ============================================================
-- 9. 優惠券
-- ============================================================
CREATE TABLE coupons (
    id               INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    code             VARCHAR(30) UNIQUE NOT NULL,
    name             VARCHAR(200) NOT NULL,
    type             ENUM('percent','fixed','shipping') DEFAULT 'fixed',
    value            DECIMAL(10,2) NOT NULL,
    min_order_amt    DECIMAL(10,2) DEFAULT 0,
    max_discount     DECIMAL(10,2),
    total_qty        INT,
    used_qty         INT DEFAULT 0,
    per_member_limit INT DEFAULT 1,
    start_at         DATETIME,
    expire_at        DATETIME,
    is_active        TINYINT(1) DEFAULT 1,
    created_at       DATETIME DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_code   (code),
    INDEX idx_expire (expire_at)
) ENGINE=InnoDB;

INSERT INTO coupons (code, name, type, value, min_order_amt, total_qty, used_qty, start_at, expire_at) VALUES
('WELCOME100', '新會員優惠 折100元',      'fixed',   100,  500, 1000, 234, NOW() - INTERVAL 60 DAY, NOW() + INTERVAL 30 DAY),
('SUMMER10',   '夏日購物節 九折優惠',     'percent',  10, 1000,  500, 187, NOW() - INTERVAL 30 DAY, NOW() + INTERVAL 15 DAY),
('FREESHIP',   '免運費優惠券',            'shipping', 60,  300, 2000, 456, NOW() - INTERVAL 90 DAY, NOW() + INTERVAL 60 DAY),
('VIP200',     'VIP 會員專屬 200元折扣',  'fixed',   200, 2000,  200,  89, NOW() - INTERVAL 10 DAY, NOW() + INTERVAL 20 DAY),
('FLASH20',    '限時閃購 八折',           'percent',  20,  500,  300, 300, NOW() - INTERVAL  7 DAY, NOW() - INTERVAL  1 DAY),
('BIRTHDAY',   '生日優惠 300元',          'fixed',   300, 1000, NULL,  45, NOW() - INTERVAL 180 DAY,NOW() + INTERVAL 180 DAY);

-- ============================================================
-- 10. 庫存異動記錄
-- ============================================================
CREATE TABLE inventory_logs (
    id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    product_id  INT UNSIGNED NOT NULL,
    change_qty  INT NOT NULL,
    before_qty  INT NOT NULL,
    after_qty   INT NOT NULL,
    reason      ENUM('purchase','sale','return','adjustment','damage') DEFAULT 'sale',
    ref_id      INT UNSIGNED,
    notes       VARCHAR(300),
    operator    VARCHAR(100) DEFAULT 'system',
    created_at  DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (product_id) REFERENCES products(id),
    INDEX idx_product (product_id),
    INDEX idx_created (created_at),
    INDEX idx_reason  (reason)
) ENGINE=InnoDB;

INSERT INTO inventory_logs (product_id, change_qty, before_qty, after_qty, reason, notes, operator, created_at)
SELECT
    p.id,
    CASE WHEN n MOD 5 = 0 THEN 50 + (n MOD 50) ELSE -(1 + n MOD 5) END,
    p.stock_qty + ABS(CASE WHEN n MOD 5 = 0 THEN -(50+(n MOD 50)) ELSE (1+n MOD 5) END),
    p.stock_qty,
    CASE WHEN n MOD 5 = 0 THEN 'purchase' ELSE 'sale' END,
    CASE WHEN n MOD 5 = 0 THEN CONCAT('採購補貨 #PO', LPAD(n,5,'0'))
         ELSE CONCAT('訂單出貨 #ORD', LPAD(n*3,5,'0')) END,
    ELT(1 + (n MOD 3), 'system','admin','warehouse'),
    NOW() - INTERVAL (n * 3) HOUR
FROM products p
CROSS JOIN (
    SELECT a.N + b.N*10 + 1 AS n
    FROM (SELECT 0 N UNION SELECT 1 UNION SELECT 2 UNION SELECT 3 UNION SELECT 4
          UNION SELECT 5 UNION SELECT 6 UNION SELECT 7 UNION SELECT 8 UNION SELECT 9) a,
         (SELECT 0 N UNION SELECT 1 UNION SELECT 2) b
    WHERE a.N + b.N*10 < 20
) nums;

-- ============================================================
-- 11. 系統日誌
-- ============================================================
CREATE TABLE system_logs (
    id         BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
    level      ENUM('DEBUG','INFO','WARN','ERROR','FATAL') DEFAULT 'INFO',
    category   VARCHAR(50),
    message    TEXT NOT NULL,
    context    JSON,
    ip_addr    VARCHAR(45),
    member_id  INT UNSIGNED,
    created_at DATETIME(3) DEFAULT CURRENT_TIMESTAMP(3),
    INDEX idx_level    (level),
    INDEX idx_category (category),
    INDEX idx_created  (created_at),
    INDEX idx_member   (member_id)
) ENGINE=InnoDB;

INSERT INTO system_logs (level, category, message, ip_addr, member_id, created_at) VALUES
('INFO',  'auth',      '用戶登入成功',                                        '203.69.128.1', 1,    NOW() - INTERVAL 2  HOUR),
('INFO',  'order',     '訂單建立成功 ORD-20250101-0001',                      '203.69.128.2', 3,    NOW() - INTERVAL 3  HOUR),
('WARN',  'payment',   '付款逾時，訂單 ORD-20250101-0005 待處理',             '203.69.128.3', 5,    NOW() - INTERVAL 4  HOUR),
('ERROR', 'inventory', '庫存不足：商品 iPhone 15 Pro Max 庫存剩餘 2 件',      NULL,           NULL, NOW() - INTERVAL 5  HOUR),
('INFO',  'auth',      '用戶登出',                                            '203.69.128.1', 1,    NOW() - INTERVAL 1  HOUR),
('INFO',  'review',    '新評論已提交 product_id=4 rating=5',                  '203.69.128.4', 6,    NOW() - INTERVAL 6  HOUR),
('WARN',  'auth',      '登入失敗：密碼錯誤 email=unknown@test.com',           '180.100.1.1',  NULL, NOW() - INTERVAL 7  HOUR),
('ERROR', 'payment',   '付款 API 連線逾時',                                   '203.69.128.5', 8,    NOW() - INTERVAL 8  HOUR),
('INFO',  'coupon',    '優惠券 SUMMER10 已套用，折扣 NT$234',                 '203.69.128.6', 10,   NOW() - INTERVAL 9  HOUR),
('FATAL', 'database',  '資料庫連線池耗盡，嘗試重新連線',                      NULL,           NULL, NOW() - INTERVAL 10 HOUR),
('INFO',  'backup',    '每日自動備份完成 backup_20250101.sql.gz',              NULL,           NULL, NOW() - INTERVAL 12 HOUR),
('WARN',  'inventory', '商品庫存低於警戒：MacBook Pro 14吋 剩餘 18 件',      NULL,           NULL, NOW() - INTERVAL 15 HOUR),
('INFO',  'member',    '新會員註冊：olivia_pan',                              '60.100.200.1', 15,   NOW() - INTERVAL 20 DAY),
('INFO',  'order',     '訂單已出貨 ORD-20250101-0010，物流 SF1234567890',     NULL,           NULL, NOW() - INTERVAL 2  DAY),
('INFO',  'order',     '訂單已完成 ORD-20250101-0003，累積點數 52',           NULL,           3,    NOW() - INTERVAL 1  DAY);

-- ============================================================
-- 12. Views
-- ============================================================
CREATE OR REPLACE VIEW v_order_summary AS
SELECT
    o.id, o.order_no,
    m.full_name    AS member_name,
    m.email,
    o.status, o.payment_status, o.total_amt,
    COUNT(oi.id)   AS item_count,
    SUM(oi.qty)    AS total_qty,
    o.created_at
FROM orders o
JOIN members m     ON m.id = o.member_id
JOIN order_items oi ON oi.order_id = o.id
GROUP BY o.id, o.order_no, m.full_name, m.email,
         o.status, o.payment_status, o.total_amt, o.created_at;

CREATE OR REPLACE VIEW v_product_stats AS
SELECT
    p.id, p.sku, p.name,
    c.name                            AS category,
    p.price, p.stock_qty, p.min_stock,
    COALESCE(SUM(oi.qty), 0)          AS total_sold,
    COALESCE(SUM(oi.subtotal), 0)     AS total_revenue,
    ROUND(COALESCE(AVG(r.rating),0),1)AS avg_rating,
    COUNT(DISTINCT r.id)              AS review_count,
    IF(p.stock_qty <= p.min_stock, '庫存警告', '正常') AS stock_status
FROM products p
LEFT JOIN categories  c  ON c.id = p.category_id
LEFT JOIN order_items oi ON oi.product_id = p.id
LEFT JOIN reviews     r  ON r.product_id  = p.id
GROUP BY p.id, p.sku, p.name, c.name, p.price, p.stock_qty, p.min_stock;

CREATE OR REPLACE VIEW v_member_stats AS
SELECT
    m.id, m.username, m.full_name, m.email,
    m.level, m.points,
    COUNT(DISTINCT o.id)              AS order_count,
    COALESCE(SUM(o.total_amt), 0)     AS total_spent,
    MAX(o.created_at)                 AS last_order_at,
    m.last_login
FROM members m
LEFT JOIN orders o ON o.member_id = m.id AND o.status != 'cancelled'
GROUP BY m.id, m.username, m.full_name, m.email,
         m.level, m.points, m.last_login;

SET foreign_key_checks = 1;

-- 完成統計
SELECT '✅ 測試資料庫建立完成！' AS 結果;
SELECT
    (SELECT COUNT(*) FROM categories)     AS 類別,
    (SELECT COUNT(*) FROM suppliers)      AS 供應商,
    (SELECT COUNT(*) FROM products)       AS 商品,
    (SELECT COUNT(*) FROM members)        AS 會員,
    (SELECT COUNT(*) FROM orders)         AS 訂單,
    (SELECT COUNT(*) FROM order_items)    AS 訂單明細,
    (SELECT COUNT(*) FROM reviews)        AS 評論,
    (SELECT COUNT(*) FROM coupons)        AS 優惠券,
    (SELECT COUNT(*) FROM inventory_logs) AS 庫存記錄,
    (SELECT COUNT(*) FROM system_logs)    AS 系統日誌;
