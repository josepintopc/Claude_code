using SupernovaBench.Core.Drivers;

namespace SupernovaBench.Core.Tests;

public class DriversTests
{
    // Trecho real de lookupValueSearch.aspx?TypeID=3 (nomes novos com "NVIDIA ", antigos sem).
    private const string ProductsXml = """
        <?xml version="1.0" encoding="UTF-8"?><LookupValueSearch><LookupValues>
        <LookupValue ParentID="1"><Name>GeForce 8800 Ultra</Name><Value>8</Value></LookupValue>
        <LookupValue ParentID="120"><Name>GeForce RTX 3060 Ti</Name><Value>934</Value></LookupValue>
        <LookupValue ParentID="120"><Name>GeForce RTX 3060</Name><Value>942</Value></LookupValue>
        <LookupValue ParentID="127"><Name>NVIDIA GeForce RTX 4070</Name><Value>1015</Value></LookupValue>
        <LookupValue ParentID="129"><Name>NVIDIA GeForce RTX 4070 Laptop GPU</Name><Value>1007</Value></LookupValue>
        <LookupValue ParentID="x"><Name>Quebrado</Name><Value>1</Value></LookupValue>
        </LookupValues></LookupValueSearch>
        """;

    // Trecho real de AjaxDriverService.php (DriverManualLookup).
    private const string LatestJson = """
        { "Success" : "1", "IDS" : [{ "downloadInfo": { "Success" : "1", "ID" : "285320", "Name" : "GeForce%20Game%20Ready%20Driver",
          "Version" : "621.24", "IsBeta" : "0", "IsWHQL" : "1", "ReleaseDateTime" : "Thu Nov 12, 2026",
          "DetailsURL" : "https://www.nvidia.com/en-us/drivers/details/285320/",
          "DownloadURL" : "https://us.download.nvidia.com/Windows/621.24/621.24-desktop-win10-win11-64bit-international-dch-whql.exe",
          "DownloadURLFileSize" : "1.04 GB" } }] }
        """;

    [Fact]
    public void Wmi_dates_categories_and_vendors()
    {
        Assert.Equal(new DateTime(2023, 5, 12), DriverInfo.ParseWmiDate("20230512000000.******+***"));
        Assert.Equal(new DateTime(2006, 6, 21), DriverInfo.ParseWmiDate("20060621000000.000000-000"));
        Assert.Null(DriverInfo.ParseWmiDate(""));
        Assert.Null(DriverInfo.ParseWmiDate("garbage!"));
        Assert.Equal(DriverCategory.Display, DriverInfo.CategoryOf("Display"));
        Assert.Equal(DriverCategory.Network, DriverInfo.CategoryOf("NET"));
        Assert.Equal(DriverCategory.Storage, DriverInfo.CategoryOf("SCSIAdapter"));
        Assert.Equal(DriverCategory.Other, DriverInfo.CategoryOf(null));
        Assert.Equal("drivers.cat.bluetooth", DriverInfo.CategoryKey(DriverCategory.Bluetooth));
        Assert.Equal("NVIDIA", DriverInfo.GpuVendor(@"PCI\VEN_10DE&DEV_2786&SUBSYS_51111458"));
        Assert.Equal("AMD", DriverInfo.GpuVendor(@"PCI\VEN_1002&DEV_744C"));
        Assert.Equal("Intel", DriverInfo.GpuVendor(@"PCI\VEN_8086&DEV_A780"));
        Assert.Null(DriverInfo.GpuVendor(@"ROOT\BasicDisplay"));
    }

    [Fact]
    public void Problem_codes_and_update_results()
    {
        Assert.Equal("drivers.problem.28", new ProblemDevice("X", "Unknown", 28, "id").ErrorKey);
        Assert.Equal("drivers.problem.28", ProblemDevice.ErrorKeyFor(18));
        Assert.Equal("drivers.problem.other", ProblemDevice.ErrorKeyFor(99));
        Assert.True(new ProblemDevice("Fone", "Bluetooth", 45, "id").IsJustDisconnected);
        Assert.True(new WuInstallResult("1", "t", 2, 0).Succeeded);
        Assert.Equal("drivers.wu.result.failed", new WuInstallResult("1", "t", 4, -1).ResultKey);
        Assert.False(new WuInstallResult("1", "t", 5, 0).Succeeded);
    }

    [Theory]
    [InlineData("32.0.15.6636", "566.36")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("32.0.16.2124", "621.24")]
    [InlineData("27.21.14.5671", "456.71")]
    public void Nvidia_version_from_windows_version(string windows, string nvidia)
        => Assert.Equal(nvidia, DriverVersions.NvidiaFromWindows(windows));

    [Fact]
    public void Version_comparison()
    {
        Assert.True(DriverVersions.Compare("566.36", "621.24") < 0);
        Assert.True(DriverVersions.Compare("621.24", "621.4") > 0);
        Assert.Equal(0, DriverVersions.Compare("31.0.101.5592", "31.0.101.5592"));
        Assert.True(DriverVersions.Compare("1.2", "1.2.1") < 0);
        Assert.Null(DriverVersions.NvidiaFromWindows("1.2"));
        Assert.Null(DriverVersions.NvidiaFromWindows(null));
    }

    [Fact]
    public void Nvidia_products_are_matched_by_name()
    {
        var products = NvidiaDriverApi.ParseProducts(ProductsXml);
        Assert.Equal(5, products.Count);
        Assert.Equal(new NvidiaProduct("NVIDIA GeForce RTX 4070", 127, 1015), NvidiaDriverApi.FindProduct(products, "NVIDIA GeForce RTX 4070"));
        Assert.Equal(942, NvidiaDriverApi.FindProduct(products, "NVIDIA GeForce RTX 3060")!.ProductId);
        Assert.Equal(934, NvidiaDriverApi.FindProduct(products, "nvidia geforce rtx 3060 ti")!.ProductId);
        Assert.Equal(1007, NvidiaDriverApi.FindProduct(products, "NVIDIA GeForce RTX 4070 Laptop GPU")!.ProductId);
        Assert.Equal(1007, NvidiaDriverApi.FindProduct(products, "NVIDIA GeForce RTX 4070 Laptop")!.ProductId);
        Assert.Null(NvidiaDriverApi.FindProduct(products, "NVIDIA GeForce RTX 9999"));

        var url = NvidiaDriverApi.LatestUrl(products[3], studio: false);
        Assert.Contains("psid=127&pfid=1015&osID=57", url);
        Assert.Contains("upCRD=0", url);
        Assert.Contains("upCRD=1", NvidiaDriverApi.LatestUrl(products[3], studio: true));
    }

    [Fact]
    public void Nvidia_latest_driver_is_parsed()
    {
        var d = NvidiaDriverApi.ParseLatest(LatestJson)!;
        Assert.Equal("621.24", d.Version);
        Assert.Equal(new DateTime(2026, 11, 12), d.Date);
        Assert.Equal("GeForce Game Ready Driver", d.Name);
        Assert.Equal("1.04 GB", d.Size);
        Assert.EndsWith("621.24-desktop-win10-win11-64bit-international-dch-whql.exe", d.DownloadUrl);
        Assert.Equal("https://www.nvidia.com/en-us/drivers/details/285320/", d.DetailsUrl);

        Assert.Null(NvidiaDriverApi.ParseLatest("""{ "Success" : "0" }"""));
        Assert.Null(NvidiaDriverApi.ParseLatest("""{ "Success" : "1", "IDS" : [] }"""));
    }
}
