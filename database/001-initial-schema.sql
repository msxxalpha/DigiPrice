/*
 DigiPrice pilot schema v1
 Run this script in SSMS after creating the empty DigiPrice database.
 It does not drop or overwrite existing tables or seller data.
*/
IF DB_NAME() <> N'DigiPrice'
    THROW 51000, 'Run this script in the DigiPrice database.', 1;
GO

IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Products
    (
        Id uniqueidentifier NOT NULL,
        SellerSku nvarchar(100) COLLATE Latin1_General_100_CI_AS NOT NULL,
        Title nvarchar(250) NOT NULL,
        CurrentPriceToman bigint NOT NULL,
        CostPriceToman bigint NOT NULL,
        MinPriceToman bigint NOT NULL,
        MaxPriceToman bigint NOT NULL,
        CompetitorPriceToman bigint NULL,
        Strategy nvarchar(64) NOT NULL,
        Mode nvarchar(64) NOT NULL,
        Enabled bit NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_Products PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_Products_CurrentPrice_Positive CHECK (CurrentPriceToman > 0),
        CONSTRAINT CK_Products_PriceBounds CHECK (MinPriceToman > 0 AND MaxPriceToman >= MinPriceToman),
        CONSTRAINT CK_Products_CurrentWithinBounds CHECK (CurrentPriceToman >= MinPriceToman AND CurrentPriceToman <= MaxPriceToman),
        CONSTRAINT CK_Products_Cost_NonNegative CHECK (CostPriceToman >= 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'IX_Products_SellerSku')
    CREATE UNIQUE INDEX IX_Products_SellerSku ON dbo.Products (SellerSku);
GO

SELECT N'DigiPrice pilot schema v1 is ready.' AS Result, DB_NAME() AS DatabaseName;
