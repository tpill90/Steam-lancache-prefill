﻿using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;
using SteamPrefill.Models.Enums;

namespace SteamPrefill.Utils
{
    public static class KeyValueExtensions
    {
        /// <summary>
        /// Attempts to convert and return the value of this instance as an unsigned long.
        /// If the conversion is invalid, null is returned.
        /// </summary>
        /// <returns>The value of this instance as an unsigned long.</returns>
        public static ulong? AsUnsignedLongNullable(this KeyValue keyValue)
        {
            ulong value;

            if (ulong.TryParse(keyValue.Value, out value) == false)
            {
                return null;
            }

            return value;
        }

        /// <summary>
        /// Attempts to convert and return the value of this instance as an unsigned int.
        /// If the conversion is invalid, null is returned.
        /// </summary>
        /// <returns>The value of this instance as an unsigned int.</returns>
        public static uint? AsUnsignedIntNullable(this KeyValue keyValue)
        {
            uint value;

            if (uint.TryParse(keyValue.Value, out value) == false)
            {
                return null;
            }

            return value;
        }

        public static DateTime? AsDateTime(this KeyValue keyValue)
        {
            long seconds;

            if (long.TryParse(keyValue.Value, out seconds) == false)
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(seconds).DateTime;
        }

        /// <summary>
        /// Attempts to convert and return the value of this instance as an enum.
        /// If the conversion is invalid, null is returned.
        /// </summary>
        public static T AsEnum<T>(this KeyValue keyValue, bool toLower = false) where T : EnumBase<T>
        {
            if (keyValue == KeyValue.Invalid)
            {
                return null;
            }
            if (string.IsNullOrEmpty(keyValue.Value))
            {
                return null;
            }
            if (toLower)
            {
                return EnumBase<T>.Parse(keyValue.Value.ToLower());
            }
            return EnumBase<T>.Parse(keyValue.Value);
        }

        public static List<string> SplitCommaDelimited(this KeyValue keyValue)
        {
            if (keyValue == KeyValue.Invalid || string.IsNullOrEmpty(keyValue.Value))
            {
                return new List<string>();
            }
            return keyValue.Value.Split(",").ToList();
        }
    }
}