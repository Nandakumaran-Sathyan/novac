-- SmartPOS schema for MySQL 8.0.16+ (CHECK constraints are enforced from 8.0.16).
-- GENERATED from the EF Core migrations; EF Core migrations remain the source of truth.
-- Regenerate with:  dotnet ef migrations script --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API -o ../database/schema.sql
-- It also records the migration in __EFMigrationsHistory, so `dotnet ef database update` treats it as applied.

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `Categories` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) NOT NULL,
    `Description` varchar(500) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Users` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `FullName` varchar(100) NOT NULL,
    `Email` varchar(254) NOT NULL,
    `PasswordHash` varchar(500) NOT NULL,
    `Role` varchar(20) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    `LastLoginAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Products` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Sku` varchar(50) NOT NULL,
    `Name` varchar(150) NOT NULL,
    `Description` varchar(1000) NULL,
    `CategoryId` int NOT NULL,
    `SellingPrice` decimal(18,2) NOT NULL,
    `CostPrice` decimal(18,2) NOT NULL,
    `StockQuantity` int NOT NULL,
    `ReorderLevel` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_Products_CostPrice_NonNegative` CHECK (CostPrice >= 0),
    CONSTRAINT `CK_Products_ReorderLevel_NonNegative` CHECK (ReorderLevel >= 0),
    CONSTRAINT `CK_Products_SellingPrice_NonNegative` CHECK (SellingPrice >= 0),
    CONSTRAINT `CK_Products_StockQuantity_NonNegative` CHECK (StockQuantity >= 0),
    CONSTRAINT `FK_Products_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `Sales` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InvoiceNumber` varchar(30) NOT NULL,
    `CashierId` int NOT NULL,
    `Subtotal` decimal(18,2) NOT NULL,
    `DiscountPercent` decimal(5,2) NOT NULL,
    `DiscountAmount` decimal(18,2) NOT NULL,
    `TaxRatePercent` decimal(5,2) NOT NULL,
    `TaxAmount` decimal(18,2) NOT NULL,
    `Total` decimal(18,2) NOT NULL,
    `PaymentMethod` varchar(20) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_Sales_DiscountPercent_Range` CHECK (DiscountPercent >= 0 AND DiscountPercent <= 100),
    CONSTRAINT `CK_Sales_Total_NonNegative` CHECK (Total >= 0),
    CONSTRAINT `FK_Sales_Users_CashierId` FOREIGN KEY (`CashierId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `SaleItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SaleId` int NOT NULL,
    `ProductId` int NOT NULL,
    `ProductName` varchar(150) NOT NULL,
    `Sku` varchar(50) NOT NULL,
    `UnitPrice` decimal(18,2) NOT NULL,
    `UnitCost` decimal(18,2) NOT NULL,
    `Quantity` int NOT NULL,
    `LineTotal` decimal(18,2) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_SaleItems_Quantity_Positive` CHECK (Quantity > 0),
    CONSTRAINT `CK_SaleItems_UnitPrice_NonNegative` CHECK (UnitPrice >= 0),
    CONSTRAINT `FK_SaleItems_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_SaleItems_Sales_SaleId` FOREIGN KEY (`SaleId`) REFERENCES `Sales` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `StockMovements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `QuantityChange` int NOT NULL,
    `QuantityAfter` int NOT NULL,
    `Type` varchar(20) NOT NULL,
    `Reason` varchar(250) NOT NULL,
    `SaleId` int NULL,
    `UserId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_StockMovements_QuantityAfter_NonNegative` CHECK (QuantityAfter >= 0),
    CONSTRAINT `FK_StockMovements_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_StockMovements_Sales_SaleId` FOREIGN KEY (`SaleId`) REFERENCES `Sales` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_StockMovements_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_Categories_Name` ON `Categories` (`Name`);

CREATE INDEX `IX_Products_CategoryId` ON `Products` (`CategoryId`);

CREATE INDEX `IX_Products_Name` ON `Products` (`Name`);

CREATE UNIQUE INDEX `IX_Products_Sku` ON `Products` (`Sku`);

CREATE INDEX `IX_SaleItems_ProductId` ON `SaleItems` (`ProductId`);

CREATE INDEX `IX_SaleItems_SaleId` ON `SaleItems` (`SaleId`);

CREATE INDEX `IX_Sales_CashierId` ON `Sales` (`CashierId`);

CREATE INDEX `IX_Sales_CreatedAt` ON `Sales` (`CreatedAt`);

CREATE UNIQUE INDEX `IX_Sales_InvoiceNumber` ON `Sales` (`InvoiceNumber`);

CREATE INDEX `IX_StockMovements_CreatedAt` ON `StockMovements` (`CreatedAt`);

CREATE INDEX `IX_StockMovements_ProductId_CreatedAt` ON `StockMovements` (`ProductId`, `CreatedAt`);

CREATE INDEX `IX_StockMovements_SaleId` ON `StockMovements` (`SaleId`);

CREATE INDEX `IX_StockMovements_UserId` ON `StockMovements` (`UserId`);

CREATE UNIQUE INDEX `IX_Users_Email` ON `Users` (`Email`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261026154829_InitialCreate', '10.0.12');

COMMIT;

