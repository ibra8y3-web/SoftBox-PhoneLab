using SoftBox.PhoneLab.Core.Help;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OfflineHelpCatalogTests
{
    [Fact]
    public void SearchFindsArabicAndEnglishArticlesByKeywords()
    {
        var adb = OfflineHelpCatalog.Search("ADB authorize USB");
        var backup = OfflineHelpCatalog.Search("SHA256 ZIP backup");
        var arabic = OfflineHelpCatalog.Search("نسخ احتياطي");

        Assert.Contains(adb, article => article.Id == "adb-authorize");
        Assert.Contains(backup, article => article.Id == "smart-backup");
        Assert.Contains(arabic, article => article.Id == "smart-backup");
    }

    [Fact]
    public void SearchDoesNotInventTopicsAndEmptySearchReturnsCatalog()
    {
        Assert.Empty(OfflineHelpCatalog.Search("unrelated-feature-not-in-catalog"));
        Assert.Equal(OfflineHelpCatalog.Articles.Count, OfflineHelpCatalog.Search("").Count);
        Assert.NotNull(OfflineHelpCatalog.Find("apk-lab"));
        Assert.NotNull(OfflineHelpCatalog.Find("screen-recording"));
        Assert.Contains(OfflineHelpCatalog.Search("scrcpy MP4 MKV"), article => article.Id == "screen-recording");
        Assert.Null(OfflineHelpCatalog.Find("unknown"));
    }

    [Fact]
    public void HelpArticlesHaveStableIdsAndDocumentCurrentLimitations()
    {
        var ids = OfflineHelpCatalog.Articles.Select(article => article.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(OfflineHelpCatalog.Articles, article =>
        {
            Assert.False(string.IsNullOrWhiteSpace(article.TitleAr));
            Assert.False(string.IsNullOrWhiteSpace(article.BodyAr));
            Assert.False(string.IsNullOrWhiteSpace(article.RelatedModuleId));
        });
        Assert.Contains(OfflineHelpCatalog.Articles, article => article.BodyAr.Contains("غير مشفر", StringComparison.Ordinal));
        Assert.Contains(OfflineHelpCatalog.Articles, article => article.BodyAr.Contains("لا يثبت", StringComparison.Ordinal));
    }
}
