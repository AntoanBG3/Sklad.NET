using Microsoft.AspNetCore.Razor.TagHelpers;
using Sklad.Helpers;

namespace Sklad.TagHelpers;

/// <summary>
/// Stacked dual-currency table cell: euro over lev. <money value="t.UnitPrice" />
/// </summary>
[HtmlTargetElement("money", TagStructure = TagStructure.WithoutEndTag)]
public class MoneyTagHelper : TagHelper
{
    public decimal Value { get; set; }

    public int Decimals { get; set; } = 2;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        output.Content.AppendHtml("<span class=\"cell-stack\">");
        output.Content.Append(Money.Euro(Value, Decimals));
        output.Content.AppendHtml("</span><span class=\"cell-sub\">");
        output.Content.Append(Money.Lev(Value, Decimals));
        output.Content.AppendHtml("</span>");
    }
}
