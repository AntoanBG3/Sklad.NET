using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Sklad.ModelBinding;

namespace Sklad.Tests;

public class DecimalBindingTests
{
    private static async Task<DefaultModelBindingContext> BindAsync(string input, bool nullable = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizer<SharedResource>>(new FakeLocalizer<SharedResource>());
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var bindingContext = new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider()
                .GetMetadataForType(nullable ? typeof(decimal?) : typeof(decimal)),
            ModelName = "UnitPrice",
            FieldName = "UnitPrice",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new FormValueProvider(BindingSource.Form,
                new FormCollection(new Dictionary<string, StringValues> { ["UnitPrice"] = input }),
                CultureInfo.InvariantCulture),
            ActionContext = new ActionContext { HttpContext = httpContext }
        };

        await new FlexibleDecimalModelBinder().BindModelAsync(bindingContext);
        return bindingContext;
    }

    private static async Task RunUnder(string cultureName, Func<Task> body)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        try { await body(); }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("189.99")]
    [InlineData("189,99")]
    public async Task Binds_dot_and_comma_decimals_under_bulgarian_culture(string input)
        => await RunUnder("bg-BG", async () =>
        {
            var ctx = await BindAsync(input);
            Assert.True(ctx.Result.IsModelSet);
            Assert.Equal(189.99m, ctx.Result.Model);
        });

    [Theory]
    [InlineData("189.99")]
    [InlineData("189,99")]
    public async Task Binds_dot_and_comma_decimals_under_english_culture(string input)
        => await RunUnder("en-GB", async () =>
        {
            var ctx = await BindAsync(input);
            Assert.True(ctx.Result.IsModelSet);
            Assert.Equal(189.99m, ctx.Result.Model);
        });

    [Fact]
    public async Task Comma_is_never_a_group_separator()
        => await RunUnder("en-GB", async () =>
        {
            var ctx = await BindAsync("1,899");
            Assert.True(ctx.Result.IsModelSet);
            Assert.Equal(1.899m, ctx.Result.Model);
        });

    [Theory]
    [InlineData("1.899,50")]
    [InlineData("1,899.50")]
    [InlineData("abc")]
    public async Task Garbage_and_grouped_input_produce_a_model_error(string input)
    {
        var ctx = await BindAsync(input);
        Assert.False(ctx.Result.IsModelSet);
        Assert.False(ctx.ModelState.IsValid);
        Assert.NotEmpty(ctx.ModelState["UnitPrice"]!.Errors);
    }

    [Fact]
    public async Task Empty_input_binds_null_for_nullable_decimal()
    {
        var ctx = await BindAsync("", nullable: true);
        Assert.True(ctx.Result.IsModelSet);
        Assert.Null(ctx.Result.Model);
    }

    [Fact]
    public async Task Raw_input_is_preserved_for_redisplay()
    {
        var ctx = await BindAsync("bad");
        Assert.Equal("bad", ctx.ModelState["UnitPrice"]!.AttemptedValue);
    }
}
