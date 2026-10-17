// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

using System.Text.Json;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class CodeSingleCommentTests
{
    [Test]
    public static void DefaultsPreserveBoundaries()
    {
        var defaults = new Settings();
        AssertDefaults(defaults.CodeSingleComments);
        var independent = new Settings();
        defaults.CodeSingleComments.Custom.Add(new CodeSingleComment { Token = "@@" });
        Assert.Equal(0, independent.CodeSingleComments.Custom.Count, "既定の空リストはインスタンスごとに独立");
        foreach (var text in new[] { "// note", "# note", "## doc", "#", "-- note", "x -- note", "  --", "rem note", " * note" })
            Assert.Equal(LineKind.Comment, LineContext.ClassifyText(text), text);
        foreach (var text in new[] { "#include", "r#type", "#region", "x--", "x-- note", "--value", "x\t-- note", "% note" })
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(text), text);
    }

    [Test]
    public static void BuiltInSwitchesAndCustomEditsAreIndependent()
    {
        var comments = new CodeSingleCommentSettings();
        comments.BuiltIn.SlashSlash = false;
        comments.BuiltIn.Hash = false;
        comments.Custom.Add(new CodeSingleComment { Token = "%", Enabled = true });
        comments.Custom.Add(new CodeSingleComment { Token = "@@" });
        foreach (var text in new[] { "% note", "x%note", "@@note" })
            Assert.Equal(LineKind.Comment, LineContext.ClassifyText(text, comments), text);
        foreach (var text in new[] { "// note", "# note" })
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(text, comments), text);
        comments.Custom.RemoveAll(c => c.Token == "@@");
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("@@ note", comments));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("-- note", comments), "独自トークンの削除は既存フラグに影響しない");
        comments.BuiltIn.DoubleHyphen = false;
        comments.BuiltIn.Rem = false;
        comments.Custom.Clear();
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("// # -- % ", comments));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("REM note", comments));
    }

    [Test]
    public static void RemBuiltInPreservesBoundariesAndCanBeDisabled()
    {
        var comments = CustomOnly();
        foreach (var prefix in new[] { "REM ", "rem ", "rEm ", "  \tReM " })
        {
            comments.BuiltIn.Rem = false;
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(prefix + "note", comments), "無効ならコード");
            Assert.Equal(LineKind.Comment, LineContext.ClassifyText(prefix + "/*\nvalue", comments), "無効なら後続のブロック開始を認識する");
            comments.BuiltIn.Rem = true;
            Assert.Equal(LineKind.Comment, LineContext.ClassifyText(prefix + "note", comments), "行頭・大文字小文字不問");
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(prefix + "/* \"\"\"\nvalue", comments), "有効な行コメント内のブロック・文字列記号を飛ばす");
        }
        foreach (var text in new[] { "REM", "REMnote", "x REM note", "REM\tnote", "REM\nvalue" })
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(text, comments), text);
        Assert.Equal(LineKind.String, LineContext.ClassifyText("x = \"REM note", comments));
    }

    [Test]
    public static void LongestMatchIgnoresDisabledTokensAndPreservesCase()
    {
        var comments = new CodeSingleCommentSettings { Custom = [new CodeSingleComment { Token = "///" }, new CodeSingleComment { Token = "NOTE" }, new CodeSingleComment { Token = "/" }] };
        Assert.Equal("///", LineContext.MatchSingleComment("/// docs", 0, true, comments));
        comments.Custom[0].Enabled = false;
        Assert.Equal("//", LineContext.MatchSingleComment("/// docs", 0, true, comments));
        comments.BuiltIn.SlashSlash = false;
        Assert.Equal("/", LineContext.MatchSingleComment("/// docs", 0, true, comments), "独自の短いトークンも利用できる");
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("NOTE text", comments));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("note text", comments));
    }

    [Test]
    public static void StringsAndExistingBlocksTakePriority()
    {
        var comments = CustomOnly(new CodeSingleComment { Token = "%" }, new CodeSingleComment { Token = "/" }, new CodeSingleComment { Token = "<" }, new CodeSingleComment { Token = "\"" });
        foreach (var text in new[] { "\"% note", "'% note", "`% note", "\"\\\"% note" })
            Assert.Equal(LineKind.String, LineContext.ClassifyText(text, comments), text);
        foreach (var text in new[] { "\"% note\"", "'% note'", "`% note`", "/* % note */x", "<!-- % note -->x" })
            Assert.Equal(LineKind.Code, LineContext.ClassifyText(text, comments), text);
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("/* block\n% note", comments));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("<!-- block\n% note", comments));
        Assert.Equal(LineKind.String, LineContext.ClassifyText("\"\"\"\n% note", comments));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("/* block\n*/x", comments));
    }

    [Test]
    public static void MultiLineScanUsesConfiguredTokens()
    {
        var comments = new CodeSingleCommentSettings();
        comments.Custom.Add(new CodeSingleComment { Token = "%", Enabled = true });
        foreach (var prefix in new[] { "//", "# ", "-- ", "%", "REM " })
            foreach (var newline in new[] { "\n", "\r", "\r\n" })
                Assert.Equal(LineKind.Code, LineContext.ClassifyText(prefix + " /* \"\"\"" + newline + "value", comments), prefix);
        comments.Custom.Single(c => c.Token == "%").Enabled = false;
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("% /*\nvalue", comments));
        comments.BuiltIn.SlashSlash = false;
        Assert.Equal(LineKind.String, LineContext.ClassifyText("// \"\"\"\nvalue", comments));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("`/*`\nvalue", comments));
    }

    [Test]
    public static void SettingsRejectInvalidAndDuplicateTokens()
    {
        foreach (var token in new[] { "", "a b", " a", "a\t", "\n", "\r", "\0", "\u007f", "日本語" })
            Reject(() => Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = token })), token);
        Reject(() => Settings.ValidateCodeSingleComments(null!), "null settings");
        Reject(() => Settings.ValidateCodeSingleComments(new() { BuiltIn = null! }), "null built-in");
        Reject(() => Settings.ValidateCodeSingleComments(new() { Custom = null! }), "null custom");
        Reject(() => Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment[] { null! })), "null entry");
        Reject(() => Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = null! })), "null token");
        Reject(() => Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = "%" }, new CodeSingleComment { Token = "%", Enabled = false })), "duplicate");
        foreach (var token in new[] { "//", "#", "--", "REM", "rem", "ReM" })
            Reject(() => Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = token, Enabled = false })), "既存トークンは無効でも登録できない");
        Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = "NOTE" }, new CodeSingleComment { Token = "note" }));
        Settings.ValidateCodeSingleComments(CustomOnly(new CodeSingleComment { Token = "!~" }));
        Settings.ValidateCodeSingleComments(CustomOnly());
    }

    [Test]
    public static void SettingsSaveLoadAndClonePreserveSeparateNodes()
    {
        WithConfig(path =>
        {
            File.WriteAllText(path, "{\"SettingsVersion\":5}");
            AssertDefaults(Settings.Load(path).CodeSingleComments);
            var original = new Settings
            {
                CodeSingleComments = new()
                {
                    BuiltIn = new() { Hash = false, Rem = false },
                    Custom = [new CodeSingleComment { Token = "%" }, new CodeSingleComment { Token = "@@", Enabled = false }],
                },
            };
            var cloned = original.Clone();
            cloned.CodeSingleComments.BuiltIn.Hash = true;
            cloned.CodeSingleComments.BuiltIn.Rem = true;
            cloned.CodeSingleComments.Custom[0].Enabled = false;
            cloned.CodeSingleComments.Custom[1].Token = "!";
            cloned.CodeSingleComments.Custom.RemoveAt(0);
            Assert.True(!original.CodeSingleComments.BuiltIn.Hash && !original.CodeSingleComments.BuiltIn.Rem, "Clone の既存フラグは独立");
            Assert.True(original.CodeSingleComments.Custom[0].Enabled, "Clone の独自要素は独立");
            Assert.Equal("@@", original.CodeSingleComments.Custom[1].Token);
            original.Save(path);
            var loaded = Settings.Load(path).CodeSingleComments;
            Assert.True(loaded.BuiltIn.SlashSlash && !loaded.BuiltIn.Hash && loaded.BuiltIn.DoubleHyphen && !loaded.BuiltIn.Rem, "既存フラグを保存");
            Assert.Equal("%", loaded.Custom[0].Token);
            Assert.True(loaded.Custom[0].Enabled && !loaded.Custom[1].Enabled, "独自 ON/OFF を保存");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var node = json.RootElement.GetProperty("CodeSingleComments");
            Assert.True(!node.GetProperty("BuiltIn").GetProperty("#").GetBoolean(), "JSON は固定のトークン名を使用する");
            Assert.Equal(4, node.GetProperty("BuiltIn").EnumerateObject().Count());
            Assert.True(!node.GetProperty("BuiltIn").GetProperty("REM").GetBoolean(), "REM の設定も保存する");
            Assert.Equal(2, node.GetProperty("Custom").GetArrayLength());
            original.CodeSingleComments.Custom.Clear();
            original.Save(path);
            Assert.Equal(0, Settings.Load(path).CodeSingleComments.Custom.Count);
            Assert.True(Settings.Load(path).CodeSingleComments.BuiltIn.SlashSlash, "独自の全削除で既存フラグを変えない");
        });
    }

    [Test]
    public static void OmittedNodesKeepDefaultsAndExplicitEmptyCustomStaysEmpty()
    {
        WithConfig(path =>
        {
            foreach (var content in new[] { "{}", "{\"CodeSingleComments\":{}}" })
            {
                File.WriteAllText(path, content);
                AssertDefaults(Settings.Load(path).CodeSingleComments);
            }
            File.WriteAllText(path, "{\"CodeSingleComments\":{\"BuiltIn\":{\"//\":false},\"Custom\":[]}}");
            var loaded = Settings.Load(path).CodeSingleComments;
            Assert.True(!loaded.BuiltIn.SlashSlash && loaded.BuiltIn.Hash && loaded.BuiltIn.DoubleHyphen && loaded.BuiltIn.Rem, "省略したフラグだけ既定値");
            Assert.Equal(0, loaded.Custom.Count);
        });
    }

    [Test]
    public static void InvalidSettingsAreRejectedBeforeSaveAndLoad()
    {
        WithConfig(path =>
        {
            const string invalid = "{\"CodeSingleComments\":{\"Custom\":[{\"Token\":\"bad token\"}]}}";
            File.WriteAllText(path, invalid);
            Reject(() => Settings.Load(path), "Load");
            Assert.Equal(invalid, File.ReadAllText(path));
            Assert.True(!File.Exists(path + ".broken"), "不正トークンを既定値へ置き換えない");
            var settings = new Settings { CodeSingleComments = CustomOnly(new CodeSingleComment { Token = "" }) };
            Reject(() => settings.Normalize(), "Normalize");
            Reject(() => settings.Save(path), "Save");
            Assert.Equal(invalid, File.ReadAllText(path), "検証失敗時は設定を書き換えない");
            foreach (var content in new[]
            {
                "{\"CodeSingleComments\":[]}",
                "{\"CodeSingleComments\":null}",
                "{\"CodeSingleComments\":{\"BuiltIn\":null}}",
                "{\"CodeSingleComments\":{\"Custom\":null}}",
                "{\"CodeSingleComments\":{\"BuiltIn\":{\"//\":\"false\"}}}",
                "{\"CodeSingleComments\":{\"BuiltIn\":{\"%\":true}}}",
                "{\"CodeSingleComments\":{\"BuiltIn\":{\"REM\":\"true\"}}}",
                "{\"CodeSingleComments\":{\"Custom\":[{\"Token\":\"rEm\"}]}}",
                "{\"CodeSingleComments\":{\"Unknown\":[]}}",
                "{\"CodeSingleComments\":{\"Custom\":[{\"Token\":12}]}}",
                "{\"CodeSingleComments\":{\"Custom\":[{\"Token\":\"%\",\"Enabled\":\"true\"}]}}",
                "{\"CodeSingleComments\":{\"BuiltIn\":{\"#\":false},\"Custom\":[{\"Token\":\"#\"}]}}",
            })
            {
                File.WriteAllText(path, content);
                Reject(() => Settings.Load(path), "固定ノード・型・トークンを検証する");
                Assert.Equal(content, File.ReadAllText(path));
                Assert.True(!File.Exists(path + ".broken"), "設定エラーを既定値へ置き換えない");
            }
        });
    }

    [Test]
    public static void GeneralMalformedJsonKeepsExistingRecovery()
    {
        foreach (var content in new[] { "null", "not json", "[]" })
            WithConfig(path =>
            {
                File.WriteAllText(path, content);
                AssertDefaults(Settings.Load(path).CodeSingleComments);
                if (content == "null") Assert.True(!File.Exists(path + ".broken"), "JSON null の既存既定値処理を維持");
                else Assert.Equal(content, File.ReadAllText(path + ".broken"), "壊れた JSON を退避する");
            });
    }

    private static CodeSingleCommentSettings CustomOnly(params CodeSingleComment[] comments) => new()
    {
        BuiltIn = new() { SlashSlash = false, Hash = false, DoubleHyphen = false, Rem = false },
        Custom = comments.ToList(),
    };

    private static void AssertDefaults(CodeSingleCommentSettings comments)
    {
        Assert.True(comments.BuiltIn.SlashSlash && comments.BuiltIn.Hash && comments.BuiltIn.DoubleHyphen && comments.BuiltIn.Rem, "既存トークンは有効");
        Assert.Equal(0, comments.Custom.Count, "独自トークンは未登録");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (CodeSingleCommentValidationException) { return; }
        Assert.True(false, $"不正値を拒否する: {message}");
    }

    private static void WithConfig(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-code-comments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(Path.Combine(directory, "config.json")); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
