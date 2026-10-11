// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class SymbolWidthTests
{
    private static CompositionText Compose(string raw, SymbolWidth width = SymbolWidth.HalfWidth,
        PunctuationStyle punctuation = PunctuationStyle.HalfWidthCommaPeriod, bool slashAsMiddleDot = false)
    {
        var text = new CompositionText(CompositionTests.Detector)
        {
            SymbolWidth = width,
            Punctuation = punctuation,
            SlashAsMiddleDot = () => slashAsMiddleDot,
        };
        foreach (var character in raw) text.Append(character);
        return text;
    }

    [Test]
    public static void SymbolWidth_AllAsciiSymbolsKeepTypedWidth()
    {
        foreach (var symbol in Enumerable.Range('!', '~' - '!' + 1).Select(c => (char)c).Where(c => !char.IsAsciiLetterOrDigit(c)))
        {
            var text = Compose("ka" + symbol);
            Assert.Equal("か" + symbol, text.AllKana(final: true), symbol.ToString());
            Assert.Equal("ka" + symbol, text.Raw, "入力した記号を保持する");
        }
    }

    [Test]
    public static void SymbolWidth_AutoKeepsExistingJapaneseSymbols()
    {
        foreach (var (raw, expected) in new[]
        {
            ("ka[]", "か「」"), ("ka`", "か｀"), ("ka\"'", "か”’"),
            ("ka-\\", "かー￥"), ("ka!?", "か！？"),
            ("ka///", "か…"), ("ka,,,", "か..."),
        })
            Assert.Equal(expected, Compose(raw, SymbolWidth.Auto, PunctuationStyle.Japanese).AllKana(final: true), raw);
    }

    [Test]
    public static void SymbolWidth_PunctuationSettingIsIndependent()
    {
        foreach (var width in new[] { SymbolWidth.Auto, SymbolWidth.HalfWidth })
        foreach (var (style, expected) in new[]
        {
            (PunctuationStyle.Japanese, "か、か。"),
            (PunctuationStyle.FullWidthCommaPeriod, "か，か．"),
            (PunctuationStyle.FullWidthCommaKuten, "か，か。"),
            (PunctuationStyle.ToutenFullWidthPeriod, "か、か．"),
            (PunctuationStyle.HalfWidthCommaPeriod, "か,か."),
        })
            Assert.Equal(expected, Compose("ka,ka.", width, style).AllKana(final: true), $"{width}/{style}");
    }

    [Test]
    public static void SymbolWidth_HalfWidthDisablesSymbolRunAndMiddleDotRewrites()
    {
        Assert.Equal("か///", Compose("ka///", slashAsMiddleDot: true).AllKana(final: true));
        Assert.Equal("か,,,", Compose("ka,,,").AllKana(final: true));
        Assert.Equal("か、、、", Compose("ka,,,", punctuation: PunctuationStyle.Japanese).AllKana(final: true));
        Assert.Equal("か/か", Compose("ka/ka", slashAsMiddleDot: true).AllKana(final: true));
        Assert.Equal("か・か", Compose("ka/ka", SymbolWidth.Auto, slashAsMiddleDot: true).AllKana(final: true));
        Assert.Equal("か,,,", Compose("ka,,,", SymbolWidth.Auto).AllKana(final: true));
    }

    [Test]
    public static void SymbolWidth_ExplicitZSequencesRemainJapaneseSymbols()
    {
        foreach (var width in new[] { SymbolWidth.Auto, SymbolWidth.HalfWidth })
        foreach (var (raw, expected) in new[]
        {
            ("z/", "・"), ("z.", "…"), ("z,", "‥"), ("z-", "～"), ("z[", "『"), ("z]", "』"),
        })
            Assert.Equal(expected, Compose(raw, width).AllKana(final: true), $"{width}/{raw}");
    }

    [Test]
    public static void SymbolWidth_HalfWidthPunctuationAppliesToKanaKeysAndNumbers()
    {
        var text = Compose("");
        text.AppendKana(',', '、');
        text.AppendKana('.', '。');
        Assert.Equal(",.", text.AllKana(final: true));
        Assert.Equal("1.5と1,000", Compose("1.5to1,000").AllKana(final: true));
        Assert.Equal("64,あ", Compose("64,a").AllKana(final: true));
    }

    [Test]
    public static void SymbolWidth_KeyboardDisplayBackspaceAndCommit()
    {
        foreach (var live in new[] { false, true })
        {
            var keyboard = new CompositionTests.Keyboard(live: live)
            {
                SymbolWidth = SymbolWidth.HalfWidth,
                Punctuation = PunctuationStyle.HalfWidthCommaPeriod,
            };
            keyboard.Type("ka[]`!");
            Assert.Equal("か[]`!", keyboard.Showing, $"live={live}");
            keyboard.Type("\b?");
            Assert.Equal("か[]`?", keyboard.Showing);
            keyboard.Type("\n");
            Assert.Equal("か[]`?", keyboard.Host.Document);
        }
    }

    [Test]
    public static void SymbolWidth_KeyboardSpaceConversionPreservesSymbols()
    {
        var keyboard = new CompositionTests.Keyboard
        {
            SymbolWidth = SymbolWidth.HalfWidth,
            Punctuation = PunctuationStyle.HalfWidthCommaPeriod,
        };
        keyboard.Type("kyou[]");
        keyboard.Press(VirtualKeys.Space);
        Assert.Equal("今日[]", keyboard.Showing);
        keyboard.Press(VirtualKeys.Return);
        Assert.Equal("今日[]", keyboard.Host.Document);
    }

    [Test]
    public static void SymbolWidth_LiveKanjiConversionPreservesSymbols()
    {
        var keyboard = new CompositionTests.Keyboard(live: true)
        {
            SymbolWidth = SymbolWidth.HalfWidth,
            Punctuation = PunctuationStyle.HalfWidthCommaPeriod,
        };
        keyboard.Type("nihongo[]`!");
        Assert.Equal("日本語[]`!", keyboard.Showing);
        keyboard.Type("\n");
        Assert.Equal("日本語[]`!", keyboard.Host.Document);
    }

    [Test]
    public static void SymbolWidth_ExplicitFullWidthSymbolCandidateIsRespected()
    {
        var keyboard = new CompositionTests.Keyboard { SymbolWidth = SymbolWidth.HalfWidth };
        keyboard.Type("[ ");
        Assert.True(keyboard.Host.View!.Candidates.Contains("［"), "全角の角括弧を明示的に選べる");
        var candidateIndex = keyboard.Host.View.Candidates.ToList().IndexOf("［");
        for (var index = 0; index < candidateIndex; index++) keyboard.Press(VirtualKeys.Space);
        Assert.Equal("［", keyboard.Showing);
        keyboard.Press(VirtualKeys.Return);
        Assert.Equal("［", keyboard.Host.Document);
    }

    [Test]
    public static void SymbolWidth_ExplicitFullWidthModeAndCandidateAreRespected()
    {
        var keyboard = new CompositionTests.Keyboard { SymbolWidth = SymbolWidth.HalfWidth };
        keyboard.Type("ka[]`");
        keyboard.Press(VirtualKeys.F9);
        Assert.Equal("ｋａ［］｀", keyboard.Showing);
        keyboard.Press(VirtualKeys.Return);
        Assert.Equal("ｋａ［］｀", keyboard.Host.Document);

        var candidates = new CompositionTests.Keyboard(moreCandidates: reading => reading == "かわ" ? ["川", "ＫＡＷＡ"] : [])
        { SymbolWidth = SymbolWidth.HalfWidth };
        candidates.Type("kawa  ");
        Assert.Equal("ＫＡＷＡ", candidates.Showing);
        candidates.Press(VirtualKeys.Return);
        Assert.Equal("ＫＡＷＡ", candidates.Host.Document);
    }

    [Test]
    public static void SymbolWidth_ProtectedBoundaryReparsePreservesWidth()
    {
        // メールの保護区間が z/ の単位の途中で終わるため、後ろの /ka[] を読み直す。
        var text = Compose("a@z/ka[]");
        Assert.True(text.Units.Any(unit => unit.Raw == "z/"), "保護境界をまたぐローマ字単位");
        var spans = text.ProtectedSpans();
        Assert.True(spans.Any(span => span.Start == 0 && span.End == 3), "メールの保護区間は a@z");
        Assert.Equal("a@z/か[]", text.Display(final: true));
    }

    [Test]
    public static void SymbolWidth_SessionUsesCurrentOptionsForEachComposition()
    {
        var settings = new Settings { SymbolWidth = SymbolWidth.HalfWidth, Punctuation = PunctuationStyle.HalfWidthCommaPeriod };
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { SymbolWidth = () => settings.SymbolWidth, Punctuation = () => settings.Punctuation }, () => settings);
        foreach (var (width, expected) in new[] { (SymbolWidth.HalfWidth, "か[]`"), (SymbolWidth.Auto, "か「」｀") })
        {
            settings.SymbolWidth = width;
            var output = "";
            foreach (var character in "ka[]`\n")
            {
                var key = character == '\n' ? VirtualKeys.Return :
                    CompositionTests.FakeHost.JisKeys.TryGetValue(character, out var jis) ? jis.Vk : char.ToUpperInvariant(character);
                var result = session.HandleKey(key, character == '\n' ? null : character, false, false, false, false);
                foreach (var edit in result.Commits)
                {
                    output = output[..(output.Length - Math.Min(edit.DeleteBefore, output.Length))] + edit.Text;
                }
            }
            Assert.Equal(expected, output, width.ToString());
        }
    }

    [Test]
    public static void SymbolWidth_SettingsDefaultCloneAndJsonRoundTrip()
    {
        Assert.Equal(SymbolWidth.Auto, new Settings().SymbolWidth);
        Assert.Equal(PunctuationStyle.Japanese, new Settings().Punctuation);
        var settings = new Settings { SymbolWidth = SymbolWidth.HalfWidth, Punctuation = PunctuationStyle.HalfWidthCommaPeriod };
        Assert.True(settings.ToJson().Contains("\"SymbolWidth\": \"HalfWidth\""), "記号幅を名前で保存する");
        Assert.True(settings.ToJson().Contains("\"Punctuation\": \"HalfWidthCommaPeriod\""), "句読点を名前で保存する");
        Assert.Equal(SymbolWidth.HalfWidth, settings.Clone().SymbolWidth);
        var path = Path.Combine(Path.GetTempPath(), $"meltype-symbol-width-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal(SymbolWidth.HalfWidth, loaded.SymbolWidth);
            Assert.Equal(PunctuationStyle.HalfWidthCommaPeriod, loaded.Punctuation);
            File.WriteAllText(path, "{}");
            Assert.Equal(SymbolWidth.Auto, Settings.Load(path).SymbolWidth, "既存設定は Auto");
        }
        finally { File.Delete(path); }
    }
}
