﻿using System.Dynamic;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace DfT.DTRO.Converters;

/// <summary>
/// Defines conversion between an <see cref="ExpandoObject"/> and its string representation in the database.
/// </summary>
public class ExpandoObjectValueConverter : ValueConverter<ExpandoObject, string>
{
    /// <summary>
    /// The single constructor.
    /// </summary>
    public ExpandoObjectValueConverter() : base(
        expando => JsonConvert.SerializeObject(expando),
        dbValue => JsonConvert.DeserializeObject<ExpandoObject>(dbValue))
    { }
}