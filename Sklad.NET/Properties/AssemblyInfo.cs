using Microsoft.Extensions.Localization;

// Assembly name is Sklad.NET but the root namespace is Sklad; without this the
// localizer computes resource names from the assembly name and never finds the
// bg satellite assembly, silently rendering English for every culture.
[assembly: RootNamespace("Sklad")]
