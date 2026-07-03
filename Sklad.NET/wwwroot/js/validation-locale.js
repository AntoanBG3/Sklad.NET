// The server binds decimals with either '.' or ',' as the decimal mark (see
// FlexibleDecimalModelBinder); the stock jQuery rules are dot-only and would
// reject valid Bulgarian comma input before it ever reaches the server.
(function ($) {
    function toNumber(value) {
        return parseFloat(String(value).replace(",", "."));
    }
    $.validator.methods.number = function (value, element) {
        return this.optional(element) || /^-?\d+([.,]\d+)?$/.test(value);
    };
    $.validator.methods.range = function (value, element, param) {
        var v = toNumber(value);
        return this.optional(element) || (v >= param[0] && v <= param[1]);
    };
})(jQuery);
