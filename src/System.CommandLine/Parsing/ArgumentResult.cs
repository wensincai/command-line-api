﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.CommandLine.Binding;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace System.CommandLine.Parsing
{
    /// <summary>
    /// A result produced when parsing an <see cref="Argument"/>.
    /// </summary>
    public class ArgumentResult : SymbolResult
    {
        private ArgumentConversionResult? _conversionResult;

        internal ArgumentResult(
            Argument argument,
            SymbolResult? parent) : base(argument, parent)
        {
            Argument = argument;
        }

        /// <summary>
        /// The argument to which the result applies.
        /// </summary>
        public Argument Argument { get; }

        internal bool IsImplicit => Argument.HasDefaultValue && Tokens.Count == 0;

        internal IReadOnlyList<Token>? PassedOnTokens { get; private set; }

        internal ArgumentConversionResult GetArgumentConversionResult() =>
            _conversionResult ??= Convert(Argument);

        /// <inheritdoc cref="GetValueOrDefault{T}"/>
        public object? GetValueOrDefault() =>
            GetValueOrDefault<object?>();

        /// <summary>
        /// Gets the parsed value or the default value for <see cref="Argument"/>.
        /// </summary>
        /// <returns>The parsed value or the default value for <see cref="Argument"/></returns>
        [return: MaybeNull]
        public T GetValueOrDefault<T>() =>
            GetArgumentConversionResult()
                .ConvertIfNeeded(this, typeof(T))
                .GetValueOrDefault<T>();

        /// <summary>
        /// Specifies the maximum number of tokens to consume for the argument. Remaining tokens are passed on and can be consumed by later arguments, or will otherwise be added to <see cref="ParseResult.UnmatchedTokens"/>
        /// </summary>
        /// <param name="numberOfTokens">The number of tokens to take. The rest are passed on.</param>
        /// <exception cref="ArgumentOutOfRangeException">numberOfTokens - Value must be at least 1.</exception>
        /// <exception cref="InvalidOperationException">Thrown if this method is called more than once.</exception>
        public void OnlyTake(int numberOfTokens)
        {
            if (numberOfTokens < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfTokens), numberOfTokens, "Value must be at least 1.");
            }

            if (PassedOnTokens is { })
            {
                throw new InvalidOperationException($"{nameof(OnlyTake)} can only be called once.");
            }

            if (numberOfTokens == 0)
            {
                return;
            }

            var passedOnTokensCount = _tokens.Count - numberOfTokens;

            PassedOnTokens = new List<Token>(_tokens.GetRange(numberOfTokens, passedOnTokensCount));

            _tokens.RemoveRange(numberOfTokens, passedOnTokensCount);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{GetType().Name} {Argument.Name}: {string.Join(" ", Tokens.Select(t => $"<{t.Value}>"))}";

        internal ParseError? CustomError(Argument argument)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                return new ParseError(ErrorMessage!, this);
            }

            for (var i = 0; i < argument.Validators.Count; i++)
            {
                var validateSymbolResult = argument.Validators[i];
                validateSymbolResult(this);

                if (!string.IsNullOrWhiteSpace(ErrorMessage))
                {
                    return new ParseError(ErrorMessage!, this);
                }
            }

            return null;
        }

        private ArgumentConversionResult Convert(Argument argument)
        {
            if (ShouldCheckArity() &&
                Parent is { } &&
                ArgumentArity.Validate(Parent,
                                       argument,
                                       argument.Arity.MinimumNumberOfValues,
                                       argument.Arity.MaximumNumberOfValues) is FailedArgumentConversionResult failedResult)
            {
                return failedResult;
            }

            if (argument is { } arg)
            {
                if (Parent!.UseDefaultValueFor(argument))
                {
                    var argumentResult = new ArgumentResult(arg, Parent);

                    var defaultValue = arg.GetDefaultValue(argumentResult);

                    if (string.IsNullOrEmpty(argumentResult.ErrorMessage))
                    {
                        return ArgumentConversionResult.Success(
                            arg,
                            defaultValue);
                    }
                    else
                    {
                        return ArgumentConversionResult.Failure(
                            arg,
                            argumentResult.ErrorMessage!);
                    }
                }

                if (arg.ConvertArguments is null)
                {
                    return argument.Arity.MaximumNumberOfValues switch
                    {
                        1 => ArgumentConversionResult.Success(argument, Tokens.Select(t => t.Value).SingleOrDefault()),
                        _ => ArgumentConversionResult.Success(argument, Tokens.Select(t => t.Value).ToArray())
                    };
                }

                if (_conversionResult is not null)
                {
                    return _conversionResult;
                }

                var success = arg.ConvertArguments(this, out var value);

                if (value is ArgumentConversionResult conversionResult)
                {
                    return conversionResult;
                }

                if (success)
                {
                    return ArgumentConversionResult.Success(
                        arg,
                        value);
                }

                if (ErrorMessage is not null)
                {
                    return new FailedArgumentConversionResult(arg, ErrorMessage);
                }

                return new FailedArgumentTypeConversionResult(
                    argument,
                    argument.ValueType,
                    Tokens[0].Value,
                    LocalizationResources);
            }

            return argument.Arity.MaximumNumberOfValues switch
            {
                1 => ArgumentConversionResult.Success(argument, Tokens.Select(t => t.Value).SingleOrDefault()),
                _ => ArgumentConversionResult.Success(argument, Tokens.Select(t => t.Value).ToArray())
            };

            bool ShouldCheckArity() => 
                Parent is not OptionResult { IsImplicit: true };
        }
    }
}
