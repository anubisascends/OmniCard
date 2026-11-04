using OmniCard.Web.Services.Dashboard;

namespace OmniCard.Tests.Web.Dashboard;

/// <summary>
/// SQL widgets may only run one read-only SELECT over the target database's dbo tables: anything that
/// could write, run code, reach another database or read sign-in secrets is refused before it runs.
/// </summary>
public class DashboardSqlValidatorTests
{
    [Theory]
    [InlineData("SELECT COUNT(*) AS Cards FROM Lots")]
    [InlineData("select p.Game, sum(l.Quantity) as Units from dbo.Lots l join Products p on p.Id = l.ProductId group by p.Game")]
    [InlineData("WITH m AS (SELECT FORMAT(OrderDate, 'yyyy-MM') AS Month, Id FROM Orders) SELECT Month, COUNT(*) AS Orders FROM m GROUP BY Month")]
    [InlineData("SELECT TOP 10 Name FROM Products ORDER BY Name; ")]
    [InlineData("SELECT Name FROM Products WITH (NOLOCK) UNION ALL SELECT Name FROM Customers")]
    [InlineData("SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS N FROM Orders WHERE Status IN (SELECT 'Shipped')")]
    [InlineData("-- a comment mentioning DELETE FROM Users\nSELECT 'DROP TABLE Lots' AS Text")]
    [InlineData("SELECT * FROM [Products]")]
    public void Accepts_ReadOnlySelects(string sql) => Assert.Null(DashboardSqlValidator.Validate(sql));

    [Theory]
    [InlineData("")]
    [InlineData("DELETE FROM Lots")]
    [InlineData("UPDATE Products SET Name = 'x'")]
    [InlineData("INSERT INTO Tags (Name) VALUES ('x')")]
    [InlineData("DROP TABLE Lots")]
    [InlineData("TRUNCATE TABLE Lots")]
    [InlineData("EXEC sp_who")]
    [InlineData("SELECT 1; DELETE FROM Lots")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT * INTO Copy FROM Lots")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("SELECT PasswordHash FROM dbo.[users]")]
    [InlineData("SELECT * FROM Lots l JOIN Users u ON u.Id = l.Id")]
    [InlineData("WITH x AS (SELECT * FROM Users) SELECT * FROM x")]
    [InlineData("SELECT * FROM OmniCard.dbo.Lots")]
    [InlineData("SELECT * FROM OtherServer.OmniCard.dbo.Lots")]
    [InlineData("SELECT * FROM sys.tables")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.COLUMNS")]
    [InlineData("SELECT * FROM syslogins")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=x;', 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENQUERY(Linked, 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=x').db.dbo.t")]
    [InlineData("SELECT * FROM Lots WITH (TABLOCKX)")]
    [InlineData("SELECT * FROM sys.dm_exec_sessions")]
    [InlineData("SELECT master.sys.fn_varbintohexstr(0x01)")]
    [InlineData("SELECT * FROM Lots WHERE")]
    public void Rejects_AnythingElse(string sql) => Assert.NotNull(DashboardSqlValidator.Validate(sql));

    [Fact]
    public void Rejects_OverlongQueries() =>
        Assert.NotNull(DashboardSqlValidator.Validate("SELECT 1 AS x" + new string(' ', DashboardSqlValidator.MaxLength)));
}
