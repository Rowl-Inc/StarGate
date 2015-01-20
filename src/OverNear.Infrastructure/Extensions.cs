using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using IO = System.IO;
using System.Security.Cryptography;
using System.Web;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Net;
using System.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using log4net;
using Murmur;

namespace OverNear.Infrastructure
{
	public static class Extensions
	{
		static readonly ILog _logger = LogManager.GetLogger(typeof(Extensions));

		static readonly DateTime __EPOCH = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		public static DateTime FromUnixTime(this int unixTime)
		{
			return __EPOCH.AddSeconds(unixTime);
		}
		public static DateTime FromUnixTime(this long unixTime)
		{
			return __EPOCH.AddSeconds(unixTime);
		}

		public static int ToUnixTime(this DateTime utc)
		{
			TimeSpan ts = utc.ToUniversalTime() - __EPOCH;
			return (int)ts.TotalSeconds;
		}

		public static string TrimToEmpty(this string s)
		{
			if (string.IsNullOrEmpty(s))
				return string.Empty;

			return s.Trim();
		}

		/// <summary>
		/// Check a collection to see if its null or empty.
		/// </summary>
		public static bool IsNullOrEmpty<T>(this IEnumerable<T> col)
		{
			return col == null || !col.Any();
		}

		/// <summary>
		/// Loop through the enumerable execute each item using the provided action
		/// </summary>
		/// <returns>original enumerable is returned for fluent style coding</returns>
		public static IEnumerable<T> ForEach<T>(this IEnumerable<T> self, Action<T> action)
		{
			if (self != null && action != null)
			{
				foreach (T item in self)
					action(item);
			}
			return self;
		}

		/// <summary>
		/// Loop through the enumerable execute each item using the provided action
		/// </summary>
		/// <returns>original enumerable is returned for fluent style coding</returns>
		public static ICollection<T> ForEach<T>(this ICollection<T> self, Action<T> action)
		{
			if (self != null && action != null)
			{
				foreach (T item in self)
					action(item);
			}
			return self;
		}

		/// <summary>
		/// take a string input and return computed sha1 in hex
		/// </summary>
		public static string ToSHA1(this string input)
		{
			using (SHA1 h = SHA1.Create())
			{
				byte[] data = h.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
				var sBuilder = new StringBuilder();
				for (int i = 0; i < data.Length; i++)
				{
					sBuilder.Append(data[i].ToString("x2"));
				}
				return sBuilder.ToString();
			}
		}

		/// <summary>
		/// Take a string input and return computed result in 32 bit int
		/// </summary>
		public static int ToMurMur3_32(this string input)
		{
			using (var h = Murmur32.Create())
			{
				byte[] data = h.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
				return BitConverter.ToInt32(data, 0);
			}
		}

		/// <summary>
		/// Extract configuration values from multiple sources, failing that, return the default value.
		/// Cascades through several sources, by first fetching the defaultValue, and then fetching from environment variables, and then configuration variables
		/// Every step will override the previous if data exists so in order of priority Config > Environment > Default
		/// </summary>
		public static T ExtractConfiguration<T>(this NameValueCollection col, string key, T defaultValue)
			where T : IConvertible
		{
			T ret = defaultValue;
			var env = ExtractEnvironmentValue<T>(key); //Environment Variables
			if(env != null && !env.Equals(default(T)))
				ret = env;
			var config = ExtractConfigValue<T>(col, key); //Configuration Variables
			if (config != null && !config.Equals(default(T)))
				ret = config;
			return ret;
		}

		public static TimeSpan ExtractConfiguration(this NameValueCollection col, string key, TimeSpan defaultValue)
		{
			TimeSpan ret = defaultValue;
			var env = ExtractEnvironmentValue<string>(key);
			TimeSpan envSpan = default(TimeSpan);
			if (!string.IsNullOrEmpty(env) && TimeSpan.TryParse(env, out envSpan) && !envSpan.Equals(default(TimeSpan)))
				ret = envSpan;
			var config = ExtractConfigValue<string>(col, key);
			TimeSpan configSpan = default(TimeSpan);
			if (!string.IsNullOrEmpty(config) && TimeSpan.TryParse(config, out configSpan) && !configSpan.Equals(default(TimeSpan)))
				ret = configSpan;
			return ret;
		}

		/// <summary>
		/// Extract and cast a value from name value collection into an appropriate type
		/// This method is meant to be used for config entry extraction of values into native types
		/// </summary>
		/// <typeparam name="T">IConvertable native type</typeparam>
		/// <param name="col">the collection</param>
		/// <param name="key">key in which to extract values from</param>
		/// <param name="defaultValue">default value to return if the extracted value is the default of T</param>
		/// <returns>extracted value of T or provided default value of T if nothing is found</returns>
		public static T ExtractConfigValue<T>(NameValueCollection col, string key)
			where T : IConvertible
		{
			T value = default(T);
			if (col != null && col.HasKeys())
			{
				string sval = col[key];
				return TryParse(sval, default(T));
			}
			return value;
		}

		static T TryParse<T>(string value, T defaultValue)
		{
			T convertedValue = defaultValue;
			if (!string.IsNullOrEmpty(value))
			{
				try
				{
					T v = default(T);
					if (!string.IsNullOrEmpty(value))
					{
						Type t = typeof(T);
						if (t.IsEnum)
							v = (T)Enum.Parse(t, value, true);
						else
							v = (T)System.Convert.ChangeType(value, typeof(T));
					}
					if (!v.Equals(default(T))) {
						convertedValue = v;
					}
				}
				catch (InvalidCastException)
				{
					//only ignore cast exceptions, equivalent of tryParse
				}
			}
			return convertedValue;
		}

		static T TryParse<T>(string value)
		{
			T convertedValue = default(T);
			if (!string.IsNullOrEmpty(value))
			{
				try
				{
					if (!string.IsNullOrEmpty(value))
					{
						Type t = typeof(T);
						if (t.IsEnum)
							convertedValue = (T)Enum.Parse(t, value, true);
						else
							convertedValue = (T)System.Convert.ChangeType(value, typeof(T));
					}
				}
				catch (InvalidCastException)
				{
					//only ignore cast exceptions, equivalent of tryParse
				}
			}
			return convertedValue;
		}

		/// <summary>
		/// Extract and cast a value from environment variables into an apropriate type
		/// This method is meant to be used for environment variable extraction of values into native types
		/// </summary>
		/// <typeparam name="T">IConvertable native type</typeparam>
		/// <param name="key">key in which to extract values from</param>
		/// <returns>extracted value or default</returns>
		public static T ExtractEnvironmentValue<T>(string key)
		{
			T result = default(T);
			string value = null;
			try
			{
				value = Environment.GetEnvironmentVariable(key);
			}
			catch (Exception)
			{
				_logger.Error("Environment Security Error");
			}
			T parseResult = default(T);
			if (value != null)
				parseResult = TryParse<T>(value);
			if (parseResult != null)
				result = parseResult;
			return result;
		}

		///// <summary>
		///// Generic version of Convert that will convert anything IConvertable
		///// </summary>
		///// <typeparam name="T">IConvertable type to be converted</typeparam>
		///// <param name="s">string value to be converted over (do a .ToString() from any object for input)</param>
		///// <returns>Converted type</returns>
		///// <exception cref="System.InvalidCastException"></exception>
		///// <exception cref="System.ArgumentNullException"></exception>
		//public static T Convert<T>(this string s)
		//	where T : IConvertible
		//{
		//	if (string.IsNullOrEmpty(s))
		//		return default(T);
		//	else
		//		return (T)System.Convert.ChangeType(s, typeof(T));
		//}

		public static bool IsDefault(this DateTime dt)
		{
			return dt == DateTime.MinValue;
		}

		public static StringBuilder AppendItems<T>(this StringBuilder sb, IEnumerable<T> items, 
			string delimiter = ",", bool skipDefault = false)
		{
			if(sb == null || items == null)
				return sb;
			if (delimiter == null)
				delimiter = string.Empty;

			int oglen = sb.Length;
			foreach (T it in items)
			{
				if (it == null)
				{
					if (skipDefault)
						continue;
				}
				else
					sb.Append(it.ToString());
				sb.Append(delimiter);
			}
			if (delimiter.Length > 0 && sb.Length >= oglen + delimiter.Length)
				sb.Length -= delimiter.Length;
			return sb;
		}

		static string ConvertToString(object item)
		{
			if (item == null || item is string)
				return null;

			if (item is System.Collections.IEnumerable)
			{
				var enums = item as System.Collections.IEnumerable;
				var olist = new List<object>();
				foreach (object o in enums)
					olist.Add(o);

				var sb = new StringBuilder();
				sb.AppendItems(olist);
				return sb.ToString();
			}
			else
				return item.ToString();
		}
		
		public static readonly JsonSerializerSettings JSON_SETTINGS = new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			DateFormatHandling = DateFormatHandling.IsoDateFormat,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			DateParseHandling = DateParseHandling.DateTime,
			DefaultValueHandling = DefaultValueHandling.Include,
			MissingMemberHandling = MissingMemberHandling.Ignore,
			TypeNameHandling = TypeNameHandling.None,
			Converters = new JsonConverter[] 
			{ 
				new IsoDateTimeConverter { DateTimeStyles = System.Globalization.DateTimeStyles.AssumeUniversal },
				new StringEnumConverter(),
			},
		};

		public static string ToJSON(this object o, bool preserveType = false)
		{
			string result = null;
			if (o != null)
			{
				result = JsonConvert.SerializeObject(o, Formatting.Indented, JSON_SETTINGS);
			}
			return result ?? string.Empty;
		}

		public static T FromJSON<T>(this string json, bool preserveType = false)
		{
			T o = default(T);
			if (!string.IsNullOrWhiteSpace(json))
			{
				o = JsonConvert.DeserializeObject<T>(json, JSON_SETTINGS);
			}
			return o;
		}

		/// <summary>
		/// Force an a pair of value key to be added or override existing value in dictionary
		/// </summary>
		/// <typeparam name="K">dictionary key type</typeparam>
		/// <typeparam name="V">dictionary value type</typeparam>
		/// <param name="key">dictionary key type</param>
		/// <param name="value">dictionary value type</param>
		/// <param name="map">dictionary of key K and value V</param>
		public static void AddOrUpdate<K, V>(this IDictionary<K, V> map, K key, V value)
		{
			if (map == null)
				return;

			if (map.ContainsKey(key))
				map[key] = value;
			else
				map.Add(key, value);
		}

		public static string ToJsonOrNullStr(this object self)
		{
			if (self == null)
				return "<null>";
			else
				return self.ToJSON();
		}

		static readonly HashSet<int> COMPLEX_PRIMATIVES = new HashSet<int>
		{
			typeof(decimal).GetHashCode(),
			typeof(string).GetHashCode(),
			typeof(TimeSpan).GetHashCode(),
			typeof(DateTime).GetHashCode(),
			typeof(byte[]).GetHashCode(),
			typeof(Guid).GetHashCode(),
		};

		public static bool SetBasicAuthHeader(this HttpWebRequest request, string credentials = null, bool noCookie = false)
		{
			bool ok = false;
			if (request != null)
			{
				if(!string.IsNullOrWhiteSpace(credentials))
					credentials = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
				else if(request.Address != null && !string.IsNullOrWhiteSpace(request.Address.UserInfo))
					credentials = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Address.UserInfo));

				if (!string.IsNullOrWhiteSpace(credentials))
				{
					request.Headers["Authorization"] = "Basic " + credentials;
					if (!noCookie)
						request.CookieContainer = CookieSingleton.Instance.Container;

					ok = true;
				}
			}
			return ok;
		}

		public static string ElasticSearchSafeBase64(this string s)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(s))
					return string.Empty;

				s = s.Trim();
				s = s.Replace('+', '-');
				s = s.Replace('/', '_');
				return s;
			}
			catch (Exception ex)
			{
				_logger.Error("ElasticSearchSafeBase64: " + (s ?? "<null>"), ex);
				throw;
			}
		}

		public static string ToMongoBase64(this byte[] arr)
		{
			try
			{
				if (arr.IsNullOrEmpty())
					return null;

				string s = arr.ToJSON();
				int start = s.IndexOf("\"");
				int ends = s.LastIndexOf("\"");
				if (start >= 0 && start >= 0)
				{
					start += 1;
					s = s.Substring(start, ends - start);
				}
				s = s.Replace('+', '-');
				s = s.Replace('/', '_');
				//s = HttpUtility.UrlDecode(s);
				return s;
			}
			catch (Exception ex)
			{
				var sb = new StringBuilder("ToMongoBase64: [");
				sb.AppendItems(arr);
				sb.Append("]");
				_logger.Error(sb.ToString(), ex);
				throw;
			}
		}

	}
}
