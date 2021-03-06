﻿//-----------------------------------------------------------------------
// <copyright file="StringExtensions.cs" company="Utilla">
//     Copyright © Utilla Contributors
// </copyright>
//-----------------------------------------------------------------------
namespace Utilla.IO
{
    using System;
    using System.Diagnostics.Contracts;
    using System.IO;

    /// <summary>
    /// Extension methods for <see cref="string"/>.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Converts the specified string to a <see cref="TextReader"/>.
        /// </summary>
        /// <param name="s">Specifies the string to convert.</param>
        /// <returns>A <see cref="TextReader"/> that reads the specified string.</returns>
        public static TextReader ToTextReader(this string s)
        {
            Contract.Requires<ArgumentNullException>(s != null, "col is null");
            return new StringReader(s);
        } // TODO: Test

        public static Stream ToStream(this string s)
        {
            Contract.Requires<ArgumentNullException>(s != null, "s is null");

            var result = new MemoryStream();
            var writer = new StreamWriter(result);
            writer.Write(s);
            writer.Flush();
            result.Position = 0;
            
            return result;
        } // Tested
    }
}
