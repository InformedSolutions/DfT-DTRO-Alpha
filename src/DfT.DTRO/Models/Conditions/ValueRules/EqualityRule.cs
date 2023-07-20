﻿using System;

namespace DfT.DTRO.Models.Conditions.ValueRules;

/// <summary>
/// Represents a rule that checks equality against a value.
/// </summary>
/// <typeparam name="T">The type of parameter used in this rule.</typeparam>
/// <param name="Value">The value to check equality against.</param>
public readonly record struct EqualityRule<T>(T Value) : IValueRule<T> where T : IComparable<T>
{
    /// <inheritdoc/>
    public bool Apply(T value)
    {
        return value.CompareTo(Value) == 0;
    }

    /// <inheritdoc/>
    public bool Contradicts(IValueRule<T> other)
    {
        if (other is null)
        {
            return false;
        }

        if (other is LessThanRule<T> lt)
        {
            return !lt.Apply(Value);
        }

        if (other is MoreThanRule<T> mt)
        {
            return !mt.Apply(Value);
        }

        if (other is EqualityRule<T> otherEquality)
        {
            return !Apply(otherEquality.Value);
        }

        if (other is InequalityRule<T> inequality)
        {
            return Apply(inequality.Value);
        }

        if (other is AndRule<T> || other is OrRule<T>)
        {
            return other.Contradicts(this);
        }

        return false;
    }

    /// <inheritdoc/>
    public IValueRule<T> Inverted()
    {
        return new InequalityRule<T>(Value);
    }

    /// <summary>
    /// Returns a string representation of this rule.
    /// </summary>
    /// <returns>A string representation of this rule</returns>
    public override string ToString()
    {
        return $"=={Value}";
    }
}
