using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace YuzuPresence
{
    public class VndbInfo
    {
        public string Id = "";
        public string Title = "";
        public string Original = "";
        public string Developer = "";
        public string Rating = "";
        public string Year = "";
        public string Length = "";
        public string ImageUrl;
        public double Sexual;
        public double Violence;
        public List<string> Aliases = new List<string>();
    }

    // JSON shapes returned by https://api.vndb.org/kana/vn
    public class VndbResponse { public List<VndbVn> results { get; set; } }
    public class VndbVn
    {
        public string id { get; set; }
        public string title { get; set; }
        public string alttitle { get; set; }
        public List<string> aliases { get; set; }
        public string released { get; set; }
        public int? length_minutes { get; set; }
        public double? rating { get; set; }
        public VndbImage image { get; set; }
        public List<VndbDeveloper> developers { get; set; }
    }
    public class VndbImage { public string url { get; set; } public double? sexual { get; set; } public double? violence { get; set; } }
    public class VndbDeveloper { public string name { get; set; } }

    public class VndbClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Regex Brackets = new Regex(@"\[[^\]]*\]|\([^\)]*\)|\u3010[^\u3011]*\u3011|\uFF08[^\uFF09]*\uFF09");
        private const string Fields = "id, title, alttitle, aliases, released, length_minutes, image.url, image.sexual, image.violence, developers.name, rating";

        private readonly Dictionary<string, VndbInfo> cache = new Dictionary<string, VndbInfo>();
        private readonly object cacheLock = new object();

        public bool LastRequestFailed { get; private set; }
        public string LastMessage { get; private set; }

        static VndbClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("YuzuPresence-Playnite/3.0");
        }

        public void ClearCache() { lock (cacheLock) { cache.Clear(); } }

        public VndbInfo GetById(string id)
        {
            LastMessage = null;
            LastRequestFailed = false;
            lock (cacheLock)
            {
                VndbInfo cached;
                if (cache.TryGetValue(id, out cached)) return cached;
            }
            var res = Query(new object[] { "id", "=", id }, 1);
            if (res.Count > 0)
            {
                Remember(res[0]);
                return res[0];
            }
            if (!LastRequestFailed) LastMessage = "VNDB has no entry " + id + ".";
            return null;
        }

        /// <summary>
        /// Searches VNDB by title. Strict mode only accepts a title, original title or alias that matches exactly
        /// (ignoring case, punctuation and bracketed text). Loose mode falls back to the top search result.
        /// </summary>
        public VndbInfo FindMatch(string gameName, bool loose)
        {
            LastMessage = null;
            LastRequestFailed = false;
            var target = Normalize(gameName);
            if (target.Length == 0) return null;

            var query = Brackets.Replace(gameName, " ").Trim();
            if (query.Length == 0) query = gameName;

            var cands = Query(new object[] { "search", "=", query }, 6);
            if (cands.Count == 0)
            {
                if (!LastRequestFailed) LastMessage = "No VNDB results for '" + query + "'.";
                return null;
            }
            foreach (var c in cands)
            {
                var names = new List<string> { c.Title, c.Original };
                names.AddRange(c.Aliases);
                if (names.Any(n => Normalize(n) == target))
                {
                    Remember(c);
                    return c;
                }
            }
            if (loose)
            {
                Remember(cands[0]);
                return cands[0];
            }
            LastMessage = "No exact title match (top result was '" + cands[0].Title + "').";
            return null;
        }

        private void Remember(VndbInfo info)
        {
            lock (cacheLock) { cache[info.Id] = info; }
        }

        private List<VndbInfo> Query(object[] filters, int count)
        {
            var list = new List<VndbInfo>();
            try
            {
                var body = Serialization.ToJson(new { filters = filters, fields = Fields, results = count });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = http.PostAsync("https://api.vndb.org/kana/vn", content).GetAwaiter().GetResult();
                var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                {
                    throw new Exception("VNDB answered " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                }
                var parsed = Serialization.FromJson<VndbResponse>(text);
                if (parsed != null && parsed.results != null)
                {
                    foreach (var vn in parsed.results) list.Add(ToInfo(vn));
                }
            }
            catch (Exception ex)
            {
                LastRequestFailed = true;
                LastMessage = ex.Message;
                logger.Warn(ex, "VNDB request failed");
            }
            return list;
        }

        private static VndbInfo ToInfo(VndbVn vn)
        {
            var info = new VndbInfo();
            info.Id = vn.id ?? "";
            info.Title = vn.title ?? "";
            info.Original = string.IsNullOrEmpty(vn.alttitle) ? info.Title : vn.alttitle;
            var dev = vn.developers != null ? vn.developers.FirstOrDefault() : null;
            info.Developer = dev != null && dev.name != null ? dev.name : "";
            if (vn.rating.HasValue) info.Rating = (vn.rating.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(vn.released) && vn.released.Length >= 4 && char.IsDigit(vn.released[0]))
                info.Year = vn.released.Substring(0, 4);
            if (vn.length_minutes.HasValue && vn.length_minutes.Value > 0)
                info.Length = Math.Round(vn.length_minutes.Value / 60.0).ToString("0", CultureInfo.InvariantCulture) + "h";
            if (vn.image != null)
            {
                info.ImageUrl = vn.image.url;
                info.Sexual = vn.image.sexual ?? 0;
                info.Violence = vn.image.violence ?? 0;
            }
            if (vn.aliases != null) info.Aliases = vn.aliases;
            return info;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Brackets.Replace(s, " ");
            s = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
