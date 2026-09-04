using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardListItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CardListId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    GameCardId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CollectorNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    FoilType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddedMarketPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsUnpriced = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardListItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Game = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TcgPlayerUsername = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddressLine1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddressLine2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ListedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OriginalLocationId = table.Column<int>(type: "int", nullable: true),
                    ListedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalRef = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrderLineId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MigrationState",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MigrationState", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MismatchLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScanHash = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    ScanImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OriginalCardId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalSetCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalConfidence = table.Column<double>(type: "float", nullable: false),
                    CorrectedCardId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectedName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectedSetCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectedNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MismatchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedMovementId = table.Column<int>(type: "int", nullable: true),
                    OrderLineId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderEdits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderEdits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    NameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConditionSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsFoilSnapshot = table.Column<bool>(type: "bit", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitSalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StageKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TrackingNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Carrier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShippingChargedToBuyer = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShippingCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MarketplaceFees = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedItemCount = table.Column<int>(type: "int", nullable: true),
                    ImportedProductValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Game = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Upc = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    GameCardId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CollectorNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rarity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Foil = table.Column<bool>(type: "bit", nullable: false),
                    FoilType = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ImageUri = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SetName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastMarketPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    PriceUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanDiagnosticEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScanHash = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanDiagnosticEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageContainers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ContainerType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CoverCardId = table.Column<int>(type: "int", nullable: true),
                    ExcludeFromDeckCheck = table.Column<bool>(type: "bit", nullable: false),
                    AlwaysAvailable = table.Column<bool>(type: "bit", nullable: false),
                    SlotsPerPage = table.Column<int>(type: "int", nullable: false),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    SheetSides = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageContainers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeSessionId = table.Column<int>(type: "int", nullable: true),
                    TradeRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Game = table.Column<int>(type: "int", nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SetName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CollectorNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Foil = table.Column<bool>(type: "bit", nullable: false),
                    IsOffDatabase = table.Column<bool>(type: "bit", nullable: false),
                    OffDbPhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OriginalLotId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FirstFulfilledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedPhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutgoingValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceivedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FirstFulfilledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScanImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Page = table.Column<int>(type: "int", nullable: true),
                    Slot = table.Column<int>(type: "int", nullable: true),
                    Section = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsMissing = table.Column<bool>(type: "bit", nullable: false),
                    FlagReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsTraded = table.Column<bool>(type: "bit", nullable: false),
                    TradeNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradePhotoPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FulfilledTradeId = table.Column<int>(type: "int", nullable: true),
                    FulfilledTradeSessionId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lots_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Lots_StorageContainers_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StorageContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LotTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EbayListings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    EbayItemId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EbayCatalogProductId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ListingType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ListedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SoldPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuctionDuration = table.Column<int>(type: "int", nullable: true),
                    BuyerUsername = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbayListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EbayListings_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlagResolutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    FlagReason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FixType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScanHash = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    FixedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlagResolutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlagResolutions_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardListItems_CardListId",
                table: "CardListItems",
                column: "CardListId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_EbayListings_EbayItemId",
                table: "EbayListings",
                column: "EbayItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EbayListings_LotId",
                table: "EbayListings",
                column: "LotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EbayListings_Status",
                table: "EbayListings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlagResolutions_LotId",
                table: "FlagResolutions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_LotId",
                table: "Listings",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Status",
                table: "Listings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_LocationId",
                table: "Lots",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ProductId",
                table: "Lots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LotTags_TagId",
                table: "LotTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_OrderLineId",
                table: "Movements",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_ProductId_Timestamp",
                table: "Movements",
                columns: new[] { "ProductId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderEdits_OrderId",
                table: "OrderEdits",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Game_Category",
                table: "Products",
                columns: new[] { "Game", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Game_GameCardId_Foil_FoilType",
                table: "Products",
                columns: new[] { "Game", "GameCardId", "Foil", "FoilType" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Upc",
                table: "Products",
                column: "Upc");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDiagnosticEvents_EventType",
                table: "ScanDiagnosticEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDiagnosticEvents_ScanHash",
                table: "ScanDiagnosticEvents",
                column: "ScanHash");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDiagnosticEvents_SessionId",
                table: "ScanDiagnosticEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StorageContainers_Name",
                table: "StorageContainers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardListItems");

            migrationBuilder.DropTable(
                name: "CardLists");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "EbayListings");

            migrationBuilder.DropTable(
                name: "FlagResolutions");

            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "LotTags");

            migrationBuilder.DropTable(
                name: "MigrationState");

            migrationBuilder.DropTable(
                name: "MismatchLogs");

            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "OrderEdits");

            migrationBuilder.DropTable(
                name: "OrderLines");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ScanDiagnosticEvents");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TradeSessions");

            migrationBuilder.DropTable(
                name: "Lots");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "StorageContainers");
        }
    }
}
