// Open in SicTerm for Windows 11's own right-click menu (docs/decisions/explorer-menu.md, tier 2).
//
// File Explorer creates this command, through the identity package's COM registration, when it builds the menu for a folder,
// the space inside a folder or a drive, and asks it for a title, an icon and its subcommands. The command runs inside
// Explorer, so it does as little as possible: it reads the menu SIC laid out in the registry (ExplorerMenu.PackageEntries,
// under HKCU\Software\Super-Intelligent-Chat\ExplorerMenu, the same shape as a classic verb key) and, when an item is chosen,
// starts the command line stored there with %V replaced by the item's path. SIC does everything else.
//
// SIC_EXPLORER_MENU_KEY names another key to read, for the smoke test, which feeds the command a scratch menu.

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>
// WRL's module storage pads itself for alignment (C4324), which level 4 reports and this project treats as an error.
#pragma warning(push)
#pragma warning(disable : 4324)
#include <wrl/implements.h>
#include <wrl/module.h>
#pragma warning(pop)
#include <algorithm>
#include <memory>
#include <string>
#include <vector>

using namespace Microsoft::WRL;

namespace
{
    constexpr wchar_t DefaultKey[] = L"Software\\Super-Intelligent-Chat\\ExplorerMenu";
    constexpr wchar_t KeyVariable[] = L"SIC_EXPLORER_MENU_KEY";

    struct Item
    {
        std::wstring title, icon, command;
        DWORD flags = 0;
    };

    struct Menu
    {
        std::wstring title, icon;
        std::vector<Item> items;
    };

    std::wstring ReadString(HKEY key, PCWSTR name)
    {
        DWORD type = 0, size = 0;
        if (RegQueryValueExW(key, name, nullptr, &type, nullptr, &size) != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ) || size == 0) return {};
        std::wstring value(size / sizeof(wchar_t), L'\0');
        if (RegQueryValueExW(key, name, nullptr, &type, reinterpret_cast<LPBYTE>(value.data()), &size) != ERROR_SUCCESS) return {};
        while (!value.empty() && value.back() == L'\0') value.pop_back();
        return value;
    }

    DWORD ReadDword(HKEY key, PCWSTR name)
    {
        DWORD value = 0, type = 0, size = sizeof(value);
        if (RegQueryValueExW(key, name, nullptr, &type, reinterpret_cast<LPBYTE>(&value), &size) != ERROR_SUCCESS || type != REG_DWORD) return 0;
        return value;
    }

    std::wstring MenuKeyPath()
    {
        wchar_t buffer[1024];
        const auto length = GetEnvironmentVariableW(KeyVariable, buffer, ARRAYSIZE(buffer));
        return length > 0 && length < ARRAYSIZE(buffer) ? std::wstring(buffer, length) : std::wstring(DefaultKey);
    }

    struct KeyHandle
    {
        HKEY key = nullptr;
        ~KeyHandle() { if (key) RegCloseKey(key); }
        bool Open(HKEY parent, PCWSTR path) { return RegOpenKeyExW(parent, path, 0, KEY_READ, &key) == ERROR_SUCCESS; }
    };

    // The items in the order of their key names, which carry their place (00new, 01hermes, ...); the registry's own order is not promised.
    Menu ReadMenu()
    {
        Menu menu;
        KeyHandle root;
        if (!root.Open(HKEY_CURRENT_USER, MenuKeyPath().c_str())) return menu;
        menu.title = ReadString(root.key, L"MUIVerb");
        menu.icon = ReadString(root.key, L"Icon");
        KeyHandle shell;
        if (!shell.Open(root.key, L"shell")) return menu;
        std::vector<std::wstring> names;
        for (DWORD index = 0;; index++)
        {
            wchar_t name[256];
            DWORD length = ARRAYSIZE(name);
            if (RegEnumKeyExW(shell.key, index, name, &length, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
            names.emplace_back(name, length);
        }
        std::sort(names.begin(), names.end());
        for (const auto& name : names)
        {
            KeyHandle itemKey;
            if (!itemKey.Open(shell.key, name.c_str())) continue;
            Item item;
            item.title = ReadString(itemKey.key, L"MUIVerb");
            item.icon = ReadString(itemKey.key, L"Icon");
            item.flags = ReadDword(itemKey.key, L"CommandFlags");
            KeyHandle commandKey;
            if (commandKey.Open(itemKey.key, L"command")) item.command = ReadString(commandKey.key, nullptr);
            if (!item.title.empty() && !item.command.empty()) menu.items.push_back(std::move(item));
        }
        return menu;
    }

    HRESULT CopyString(const std::wstring& text, PWSTR* result)
    {
        return SHStrDupW(text.c_str(), result);
    }

    extern "C" IMAGE_DOS_HEADER __ImageBase;

    std::wstring ThisModulePath()
    {
        wchar_t buffer[MAX_PATH * 2];
        const auto length = GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), buffer, ARRAYSIZE(buffer));
        return length > 0 && length < ARRAYSIZE(buffer) ? std::wstring(buffer, length) : std::wstring();
    }

    // The menus follow Windows' app mode (Choose your default app mode), not the taskbar's; 1 is light, and the default.
    bool LightMenus()
    {
        KeyHandle personalize;
        if (!personalize.Open(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) return true;
        DWORD value = 1, type = 0, size = sizeof(value);
        if (RegQueryValueExW(personalize.key, L"AppsUseLightTheme", nullptr, &type, reinterpret_cast<LPBYTE>(&value), &size) != ERROR_SUCCESS || type != REG_DWORD) return true;
        return value != 0;
    }

    bool EndsWithNoCase(const std::wstring& text, PCWSTR suffix)
    {
        const auto length = wcslen(suffix);
        return text.size() >= length && _wcsnicmp(text.c_str() + text.size() - length, suffix, length) == 0;
    }

    // What the menu is given for an icon. Windows 11's menu loads icons from PE resources ("path,-id"), not from .ico files, so
    // the agents' letter tiles and SIC's mark come from this DLL's own resources (SicExplorerCommand.rc); the mark in the set for the
    // menu's tone, light or dark, decided now rather than when SIC wrote the layout. An exe shows its own icon from "path,0";
    // anything else stands as it is.
    std::wstring IconReference(const std::wstring& icon)
    {
        static const struct { PCWSTR file; int id; } agents[] = { { L"\\claude.ico", 101 }, { L"\\codex.ico", 102 }, { L"\\hermes.ico", 103 }, { L"\\opencode.ico", 104 } };
        const auto module = ThisModulePath();
        if (!module.empty())
        {
            if (EndsWithNoCase(icon, L"\\SicTerm.exe") || EndsWithNoCase(icon, L"\\sic.ico") || EndsWithNoCase(icon, L"\\sic-light.ico")) return module + (LightMenus() ? L",-2" : L",-1");
            for (const auto& agent : agents) if (EndsWithNoCase(icon, agent.file)) return module + L",-" + std::to_wstring(agent.id);
        }
        if (EndsWithNoCase(icon, L".exe")) return icon + L",0";
        return icon;
    }

    // The folder the user right-clicked: the selected folder or drive, or for the space inside a folder, that folder.
    std::wstring ItemPath(IShellItemArray* items)
    {
        if (items == nullptr) return {};
        ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(0, &item))) return {};
        PWSTR path = nullptr;
        if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) || path == nullptr) return {};
        std::wstring result(path);
        CoTaskMemFree(path);
        return result;
    }

    // Starts SIC as a plain desktop process. This command runs in the identity package's app container, and a child would
    // inherit it: such a SIC has a named-object namespace of its own and lives by the container's rules (October 7, 2026, a
    // second SIC on the same data folder). The desktop app policy lets the child, and its own children, break away.
    void Start(const std::wstring& commandTemplate, const std::wstring& path)
    {
        std::wstring command = commandTemplate;
        for (auto at = command.find(L"%V"); at != std::wstring::npos; at = command.find(L"%V", at + path.size())) command.replace(at, 2, path);
        std::vector<wchar_t> mutableCommand(command.begin(), command.end());
        mutableCommand.push_back(L'\0');
        SIZE_T size = 0;
        InitializeProcThreadAttributeList(nullptr, 1, 0, &size);
        std::vector<BYTE> attributeStorage(size);
        auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attributeStorage.data());
        if (!InitializeProcThreadAttributeList(attributes, 1, 0, &size)) return;
        DWORD policy = PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE;
        const auto breakaway = UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY, &policy, sizeof(policy), nullptr, nullptr);
        STARTUPINFOEXW startup{};
        startup.StartupInfo.cb = sizeof(startup);
        startup.lpAttributeList = breakaway ? attributes : nullptr;
        PROCESS_INFORMATION process{};
        if (CreateProcessW(nullptr, mutableCommand.data(), nullptr, nullptr, FALSE, CREATE_UNICODE_ENVIRONMENT | (breakaway ? EXTENDED_STARTUPINFO_PRESENT : 0), nullptr, nullptr, &startup.StartupInfo, &process))
        {
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }
        DeleteProcThreadAttributeList(attributes);
    }

    // One item of the submenu: an agent, PowerShell, or New session….
    class ItemCommand final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
    {
    public:
        HRESULT RuntimeClassInitialize(const Item& item) { _item = item; return S_OK; }

        IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override { return CopyString(_item.title, title); }
        IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { return _item.icon.empty() ? E_NOTIMPL : CopyString(IconReference(_item.icon), icon); }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) override { *tooltip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = GUID_NULL; return S_OK; }
        IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = ECS_ENABLED; return S_OK; }
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
        {
            const auto path = ItemPath(items);
            if (path.empty()) return S_OK;
            Start(_item.command, path);
            return S_OK;
        }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = _item.flags & (ECF_SEPARATORBEFORE | ECF_SEPARATORAFTER); return S_OK; }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override { *commands = nullptr; return E_NOTIMPL; }

    private:
        Item _item;
    };

    class ItemEnumerator final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
    {
    public:
        HRESULT RuntimeClassInitialize(std::vector<ComPtr<IExplorerCommand>> items, size_t index = 0) { _items = std::move(items); _index = index; return S_OK; }

        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
        {
            ULONG taken = 0;
            while (taken < count && _index < _items.size()) _items[_index++].CopyTo(&commands[taken++]);
            if (fetched) *fetched = taken;
            return taken == count ? S_OK : S_FALSE;
        }
        IFACEMETHODIMP Skip(ULONG count) override { _index = (std::min)(_items.size(), _index + count); return S_OK; }
        IFACEMETHODIMP Reset() override { _index = 0; return S_OK; }
        IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override { return MakeAndInitialize<ItemEnumerator>(clone, _items, _index); }

    private:
        std::vector<ComPtr<IExplorerCommand>> _items;
        size_t _index = 0;
    };

    // Open in SicTerm itself: the flyout. Hidden when SIC has laid out no menu, so a stale package shows nothing rather than an empty entry.
    class __declspec(uuid("4D6B3C9A-2F1E-4A8B-B7C5-0E9D1A2B3C4D")) OpenInSicCommand final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
    {
    public:
        OpenInSicCommand() : _menu(ReadMenu()) {}

        IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override { return CopyString(_menu.title.empty() ? L"Open in SicTerm" : _menu.title, title); }
        IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { return _menu.icon.empty() ? E_NOTIMPL : CopyString(IconReference(_menu.icon), icon); }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) override { *tooltip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = GUID_NULL; return S_OK; }
        IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = _menu.items.empty() ? ECS_HIDDEN : ECS_ENABLED; return S_OK; }
        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return S_OK; }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_HASSUBCOMMANDS; return S_OK; }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            std::vector<ComPtr<IExplorerCommand>> items;
            for (const auto& item : _menu.items)
            {
                ComPtr<IExplorerCommand> command;
                if (SUCCEEDED(MakeAndInitialize<ItemCommand>(&command, item))) items.push_back(command);
            }
            return MakeAndInitialize<ItemEnumerator>(commands, std::move(items));
        }

    private:
        Menu _menu;
    };

    CoCreatableClass(OpenInSicCommand);
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, object);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
