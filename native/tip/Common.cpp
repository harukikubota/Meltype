// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai
//
// stub (MeltypeTip.dll) と impl (MeltypeTipImpl.dll) の両方にリンクする、文字コードの変換とログ。

#include <strsafe.h>

#include <cstdarg>
#include <cstdio>

#include "Globals.h"

namespace meltype {

HINSTANCE g_module = nullptr;

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string out(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size, nullptr, nullptr);
    return out;
}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), size);
    return out;
}

// 自分 (IME を読み込んだアプリ) の整合性レベルが Medium 以下か。読めなければ false
// (管理者として動くアプリは、ふつうの権限のプログラムがジャンクションで向けた先にログを作ってしまうので書かない)
static bool SelfIsMediumOrLower() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return false;
    DWORD level = 0;
    DWORD size = 0;
    GetTokenInformation(token, TokenIntegrityLevel, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    if (size > 0 && GetTokenInformation(token, TokenIntegrityLevel, buffer.data(), size, &size)) {
        PSID sid = reinterpret_cast<TOKEN_MANDATORY_LABEL*>(buffer.data())->Label.Sid;
        level = *GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1);
    }
    CloseHandle(token);
    return level != 0 && level <= SECURITY_MANDATORY_MEDIUM_RID;
}

static void WriteLog(const wchar_t* filename, const wchar_t* format, va_list args, bool routing) {
    // 検証時の JSON 応答も途中で切らない。通常の tip.log は従来の長さのまま。
    va_list measured;
    va_copy(measured, args);
    const int length = routing ? _vscwprintf(format, measured) : 1023;
    va_end(measured);
    if (length < 0) return;
    std::vector<wchar_t> message(static_cast<size_t>(length) + 1);
    StringCchVPrintfW(message.data(), message.size(), format, args);
    wchar_t path[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) == 0) return;
    StringCchCatW(path, MAX_PATH, L"\\Meltype\\");
    StringCchCatW(path, MAX_PATH, filename);
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME now;
    GetLocalTime(&now);
    wchar_t exe[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exe, MAX_PATH);
    const wchar_t* name = wcsrchr(exe, L'\\');
    std::vector<wchar_t> line(message.size() + 512);
    if (routing) {
        StringCchPrintfW(line.data(), line.size(), L"%04d-%02d-%02d %02d:%02d:%02d.%03d [%s pid=%lu tid=%lu tick=%llu] %s\r\n",
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, name ? name + 1 : exe,
            GetCurrentProcessId(), GetCurrentThreadId(), GetTickCount64(), message.data());
    } else {
        StringCchPrintfW(line.data(), line.size(), L"%02d:%02d:%02d.%03d [%s %lu] %s\r\n",
            now.wHour, now.wMinute, now.wSecond, now.wMilliseconds, name ? name + 1 : exe, GetCurrentThreadId(), message.data());
    }
    std::string utf8 = ToUtf8(line.data());
    DWORD written = 0;
    WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
    CloseHandle(file);
}

bool KeyRoutingLogEnabled() {
    static const bool enabled = [] {
        if (!SelfIsMediumOrLower()) return false;
        wchar_t path[MAX_PATH] = {};
        if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) == 0) return false;
        StringCchCatW(path, MAX_PATH, L"\\Meltype\\key-routing-log");
        return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES;
    }();
    return enabled;
}

void KeyRoutingLog(const wchar_t* format, ...) {
    const DWORD error = GetLastError();
    if (KeyRoutingLogEnabled()) {
        va_list args;
        va_start(args, format);
        WriteLog(L"key-routing-tip.log", format, args, true);
        va_end(args);
    }
    SetLastError(error);
}

void TipLog(const wchar_t* format, ...) {
    static int enabled = -1;
    if (enabled < 0 && !SelfIsMediumOrLower()) enabled = 0;
    if (enabled < 0) {
        wchar_t value[8] = {};
        enabled = GetEnvironmentVariableW(L"MELTYPE_TIP_LOG", value, 8) > 0 && value[0] == L'1' ? 1 : 0;
        // 環境変数が無ければ、データフォルダーに tip-log という空のファイルがあるときだけ
        if (!enabled) {
            wchar_t path[MAX_PATH] = {};
            if (GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH) > 0) {
                StringCchCatW(path, MAX_PATH, L"\\Meltype\\tip-log");
                enabled = GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES ? 1 : 0;
            }
        }
    }
    if (!enabled) return;
    va_list args;
    va_start(args, format);
    WriteLog(L"tip.log", format, args, false);
    va_end(args);
}

}  // namespace meltype
