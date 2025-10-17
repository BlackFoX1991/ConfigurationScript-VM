﻿using System.Text;

namespace CFGS_VM.VMCore.Extensions
{
    /// <summary>
    /// Defines the <see cref="VMException" />
    /// </summary>
    public sealed class VMException(string message, int line, int column, string? fileSource)
    : Exception(BuildMessage(message, line, column, fileSource))
    {
        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; } = line;

        /// <summary>
        /// Gets the Column
        /// </summary>
        public int Column { get; } = column;

        /// <summary>
        /// Gets the FileSource
        /// </summary>
        public string? FileSource { get; } = fileSource;

        /// <summary>
        /// The BuildMessage
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="column">The column<see cref="int"/></param>
        /// <param name="fileSource">The fileSource<see cref="string?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string BuildMessage(string message, int line, int column, string? fileSource)
        {
            StringBuilder sb = new();
            if (!string.IsNullOrEmpty(message))
            {
                sb.Append(message.TrimEnd());
                if (!message.TrimEnd().EndsWith(".")) sb.Append('.');
            }

            bool hasLine = line >= 0;
            bool hasCol = column >= 0;

            if (hasLine && hasCol) sb.Append($" ( Line : {line}, Column : {column} )");
            else if (hasLine) sb.Append($" ( Line : {line} )");
            else if (hasCol) sb.Append($" ( Column : {column} )");

            if (!string.IsNullOrWhiteSpace(fileSource))
                sb.Append($" [Source : '{fileSource}']");

            return sb.ToString();
        }
    }
}
