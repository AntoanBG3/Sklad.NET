using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Sklad.TagHelpers;

namespace Sklad.Tests;

public class MoneyTagHelperTests
{
    private static string Render(decimal value, int decimals = 2)
    {
        var helper = new MoneyTagHelper { Value = value, Decimals = decimals };
        var context = new TagHelperContext([], new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("money", [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(context, output);
        return output.Content.GetContent(HtmlEncoder.Create(UnicodeRanges.All));
    }

    [Fact]
    public void Renders_stacked_euro_over_lev()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        try
        {
            var html = Render(189.99m);
            Assert.Contains("<span class=\"cell-stack\">189.99 €</span>", html);
            Assert.Contains("<span class=\"cell-sub\">371.59 лв.</span>", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Respects_decimals_setting()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        try
        {
            Assert.Contains(">190 €<", Render(189.99m, decimals: 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
