using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine;
using TMPro;
using System.Diagnostics;
using System.Runtime.Remoting.Messaging;

namespace CoinTextTMPFormatter
{
    [BepInPlugin("cn.cointext-tmp-cn-units", "CoinText TMP CN Units", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static readonly string[] TargetPaths =
        {
        "Camera Ui Global/Canvas/General UI/Holder/CoinsHolder/CoinsBackImage/CoinsText",
        "Camera Ui Global/Canvas/PromptGuide/Holder/Text (TMP)",
        "Camera Ui Global/Canvas/Dialogue/Holder/Text (TMP)",
        "Camera Ui Global/Canvas/Stats/StatsHolder/StatsScaler/Stats Text",
        "Room/ATM/Screen Canvas/Holder/Txt Reach Coins",
        "Room/ATM/Screen Canvas/Holder/Txt Coins Dep",
        "Room/SLOT TABLE/SlotMachine/MeshHolder/Screens/MainSreen/Spin Total Coins Screen/Total Coins Text",
        "Room/SLOT TABLE/SlotMachine/ScreenCamera/Canvas/ScoreTextTemplate(Clone)",
        "Room/SLOT TABLE/SlotMachine/MeshHolder/Screens/TopScreen/Led Text",
        "Room/Store/Refresh Store Box/Holder/Text Price",
        "Room/ATM/Screen Canvas/Holder/Txt Interest",
    };

        private static string[] _normTargets;

        private void Awake()
        {
            Log = Logger;

            _normTargets = TargetPaths.Select(NormalizePath).ToArray();

            new Harmony(Info.Metadata.GUID).PatchAll();
            Logger.LogInfo("[CoinText CN Units] Loaded. Create by 欢歌小鱼");
        }

        private void Start()
        {
            StartCoroutine(AttachWatcherNextFrame());
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F9))
            {
                // GameplayData.CoinsAdd(new BigInteger(10000), true);
                // Logger.LogInfo("Coins Added!");
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.F10))
            {
                // GameplayData.CloverTicketsAdd(1000, true);
                // Logger.LogInfo("Tickets Added!");
            }
        }

        private System.Collections.IEnumerator AttachWatcherNextFrame()
        {
            yield return null;

            var all = GameObject.FindObjectsOfType<TMP_Text>(true);
            int attached = 0;
            foreach (var t in all)
            {
                if (IsTarget(t.transform))
                {
                    if (t.gameObject.GetComponent<CnUnitWatcher>() == null)
                    {
                        t.gameObject.AddComponent<CnUnitWatcher>();
                        attached++;
                    }
                }
            }
            if (attached > 0)
                Logger.LogInfo($"[CN Units] Watcher attached x{attached}.");
        }

        internal static string GetFullPath(Transform t)
        {
            var list = new System.Collections.Generic.List<string>(8);
            while (t != null) { list.Add(t.name); t = t.parent; }
            list.Reverse();
            return string.Join("/", list);
        }

        private static string NormalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return string.Empty;
            p = p.Replace('\\', '/').Trim();
            if (p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        internal static bool IsTarget(Transform t)
        {
            try
            {
                var full = NormalizePath(GetFullPath(t));
                for (int i = 0; i < _normTargets.Length; i++)
                {
                    if (full.EndsWith(_normTargets[i], StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            catch { return false; }
        }
    }

    public class CnUnitWatcher : MonoBehaviour
    {
        internal static class CnNum
        {
            private static readonly string[] Units = { "", "万", "亿", "兆", "京", "垓", "秭", "穰", "沟", "涧", "正", "载", "极", "恒河沙", "阿僧祇", "那由他", "不可思议", "无量", "大数", "千大数","千万大数","千亿大数", "千兆大数", "千垓大数", "千兆大数", "古戈尔","千古戈尔", "千万古戈尔", "千亿古戈尔","千兆古戈尔", "千京古戈尔", "千垓古戈尔", "千秭古戈尔", "千穰古戈尔", "千沟古戈尔", "千涧古戈尔", "千正古戈尔", "千载古戈尔", "千极古戈尔", "千恒河沙古戈尔", "千阿僧祇古戈尔", "千那由他古戈尔", "千不可思议古戈尔", "千无量古戈尔", "千大数古戈尔", "千万大数古戈尔", "千亿大数古戈尔", "千兆大数古戈尔", "千京大数古戈尔", "千垓大数古戈尔", "千古戈尔古戈尔" };

            private static readonly Regex Sci = new Regex(@"[+-]?\d+(?:\.\d+)?[eE]\+?\d+", RegexOptions.Compiled);
            private static readonly Regex Plain = new Regex(@"[+-]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?", RegexOptions.Compiled);

            [ThreadStatic] private static bool _reent;

            public static bool TryRewriteInPlace(string original, out string rewritten)
            {
                rewritten = original;
                if (string.IsNullOrEmpty(original)) return false;

                var matches = Sci.Matches(original).Cast<Match>().Concat(Plain.Matches(original).Cast<Match>()).ToList();
                if (matches.Count == 0) return false;

                Match best = null; int bestPow = int.MinValue;
                foreach (var m in matches)
                {
                    if (!TryTokenToMantExp(m.Value, out var mantInt, out var mantScale, out var exp10))
                        continue;

                    int truePow = exp10 - mantScale;
                    int powApprox = (mantInt.IsZero ? 0 : (mantInt.ToString().Length - 1)) + truePow;
                    if (powApprox > bestPow) { best = m; bestPow = powApprox; }
                }
                if (best == null || bestPow < 8) return false;

                if (!FormatTokenToCn(best.Value, out var formatted)) return false;

                rewritten = original.Substring(0, best.Index) + formatted + original.Substring(best.Index + best.Length);
                return true;
            }

            private static bool FormatTokenToCn(string token, out string output)
            {
                output = token;
                if (!TryTokenToMantExp(token, out var mant, out var scale, out var exp10))
                    return false;

                int truePow = exp10 - scale;
                int digitsLen = mant.IsZero ? 1 : mant.ToString().Length;
                int approxPow = truePow >= 0 ? (digitsLen - 1 + truePow) : (truePow - (digitsLen - 1));

                int unitIdx = Math.Max(0, approxPow / 4);
                if (unitIdx < 2) return false;
                if (unitIdx >= Units.Length) unitIdx = Units.Length - 1;

                int cutExp = unitIdx * 4;
                int showExp = exp10 - scale - cutExp;

                string num = ToDecimalString(mant, showExp, 2, true);
                output = num + Units[unitIdx];
                return true;
            }

            private static bool TryTokenToMantExp(string token, out BigInteger mantissaInt, out int mantissaScale, out int exp10)
            {
                mantissaInt = BigInteger.Zero; mantissaScale = 0; exp10 = 0;

                if (Sci.IsMatch(token))
                {
                    return ParseScientific(token, out mantissaInt, out mantissaScale, out exp10);
                }
                else if (Plain.IsMatch(token))
                {
                    var s = token.Replace(",", "");
                    int dot = s.IndexOf('.');
                    string digits = dot >= 0 ? s.Remove(dot, 1) : s;
                    mantissaScale = dot >= 0 ? (s.Length - dot - 1) : 0;
                    exp10 = 0;
                    if (!BigInteger.TryParse(digits.Replace("+", ""), out mantissaInt)) return false;
                    if (s.StartsWith("-")) mantissaInt = -mantissaInt;
                    return true;
                }
                return false;
            }

            private static bool ParseScientific(string s, out BigInteger mantissaInt, out int mantissaScale, out int exp10)
            {
                mantissaInt = BigInteger.Zero; mantissaScale = 0; exp10 = 0;
                try
                {
                    var parts = s.Trim().Split(new[] { 'e', 'E' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) return false;

                    string mant = parts[0];
                    string exps = parts[1].Replace("+", "");
                    if (!int.TryParse(exps, out exp10)) return false;

                    bool neg = mant.StartsWith("-");
                    mant = mant.TrimStart('+', '-');

                    int dot = mant.IndexOf('.');
                    string digits = (dot >= 0) ? mant.Remove(dot, 1) : mant;
                    mantissaScale = (dot >= 0) ? (mant.Length - dot - 1) : 0;

                    if (!BigInteger.TryParse(digits, out mantissaInt)) return false;
                    if (neg) mantissaInt = -mantissaInt;
                    return true;
                }
                catch { return false; }
            }

            private static string ToDecimalString(BigInteger baseInt, int showExp, int scale, bool doRound)
            {
                string abs = (baseInt.Sign < 0 ? (-baseInt).ToString() : baseInt.ToString());
                if (showExp >= 0) abs += new string('0', showExp);
                return ApplyScale(abs, showExp < 0 ? -showExp : 0, scale, baseInt.Sign < 0, doRound);
            }

            private static string ApplyScale(string digits, int fracExisting, int scale, bool negative, bool doRound)
            {
                if (fracExisting <= 0) return negative ? "-" + digits : digits;

                if (digits.Length <= fracExisting)
                    digits = "0" + new string('0', fracExisting - digits.Length) + digits;

                int intLen = digits.Length - fracExisting;
                string intPart = intLen > 0 ? digits.Substring(0, intLen) : "0";
                string fracPart = digits.Substring(intLen);

                if (fracPart.Length > scale)
                {
                    if (doRound && scale >= 0)
                    {
                        string keep = scale > 0 ? fracPart.Substring(0, scale) : "";
                        char next = fracPart[scale];
                        if (next >= '5')
                        {
                            var bi = BigInteger.Parse(intPart + keep);
                            bi += BigInteger.One;
                            string nc = bi.ToString();
                            if (scale > 0)
                            {
                                if (nc.Length <= scale) nc = nc.PadLeft(scale + 1, '0');
                                intPart = nc.Substring(0, nc.Length - scale);
                                keep = nc.Substring(nc.Length - scale);
                            }
                            else
                            {
                                intPart = nc; keep = "";
                            }
                        }
                        fracPart = keep;
                    }
                    else
                    {
                        fracPart = scale > 0 ? fracPart.Substring(0, scale) : "";
                    }
                }

                if (fracPart.Length > 0) fracPart = fracPart.TrimEnd('0');
                string res = fracPart.Length > 0 ? (intPart + "." + fracPart) : intPart;
                return negative ? "-" + res : res;
            }

            public static bool SafeSet(Action<string> setter, string text)
            {
                if (_reent) return false;
                try { _reent = true; setter(text); return true; }
                finally { _reent = false; }
            }
        }

        [HarmonyPatch(typeof(TMP_Text), "set_text")]
        internal static class Patch_TMP_set_text
        {
            static void Postfix(TMP_Text __instance)
            {
                if (!Plugin.IsTarget(__instance.transform)) return;
                var cur = __instance.text;
                if (CnNum.TryRewriteInPlace(cur, out var nw) && nw != cur)
                    CnNum.SafeSet(v => __instance.SetText(v), nw);
            }
        }

        [HarmonyPatch]
        internal static class Patch_TMP_All_SetText_Postfix
        {
            static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
                => typeof(TMP_Text).GetMethods().Where(m => m.Name == "SetText");

            static void Postfix(TMP_Text __instance)
            {
                if (!Plugin.IsTarget(__instance.transform)) return;
                var cur = __instance.text;
                if (CnNum.TryRewriteInPlace(cur, out var nw) && nw != cur)
                    CnNum.SafeSet(v => __instance.SetText(v), nw);
            }
        }
    }
}