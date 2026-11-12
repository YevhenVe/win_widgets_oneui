using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace DesktopWidgets;

public enum AutostartMethod
{
    Off,
    Shortcut, // Startup folder
    Registry, // HKCU Run key
    Task,     // Task Scheduler, at sign-in
}

/// <summary>
/// Start with Windows in one of three ways. Some PCs (or antivirus programs) skip or block one of them,
/// so the user can pick another. Only one is active at a time; all of them run for this user only and
/// need no administrator rights.
/// </summary>
public static class Autostart
{
    private const string Name = "DesktopWidgets";
    private const string ShortcutName = "Desktop Widgets.lnk";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    /// <summary>The method that is set up and not disabled in Task Manager, read from Windows each time.</summary>
    public static AutostartMethod Current
    {
        get
        {
            try
            {
                if (TaskTarget() is not null && TaskEnabled())
                    return AutostartMethod.Task;
                if (File.Exists(ShortcutPath) && Approved("StartupFolder", ShortcutName))
                    return AutostartMethod.Shortcut;
                if (RunTarget() is not null && Approved("Run", Name))
                    return AutostartMethod.Registry;
            }
            catch (Exception)
            {
                // Unreadable state shows as off.
            }
            return AutostartMethod.Off;
        }
    }

    public static void Set(AutostartMethod method)
    {
        try
        {
            RemoveAll();
            if (Environment.ProcessPath is not { } path)
                return;
            switch (method)
            {
                case AutostartMethod.Shortcut:
                    CreateShortcut(path);
                    break;
                case AutostartMethod.Registry:
                    using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                        run.SetValue(Name, $"\"{path}\"");
                    break;
                case AutostartMethod.Task:
                    CreateTask(path);
                    break;
            }
        }
        catch (Exception)
        {
            // Blocked (often by antivirus): the menu shows what actually ended up set.
        }
    }

    /// <summary>
    /// Each method stores this exe's path, so after an update (or a move) it pointed to the old copy.
    /// Points whatever is set up at the running exe again. Development builds leave it alone.
    /// </summary>
    public static void RefreshPath()
    {
        var path = Environment.ProcessPath;
        if (path is null || WidgetWindow.Preview || path.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            if (TaskTarget() is { } task && !SamePath(task, path))
                CreateTask(path);
            if (File.Exists(ShortcutPath) && !SamePath(ShortcutTarget(), path))
                CreateShortcut(path);
            if (RunTarget() is { } run && !SamePath(run, path))
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(Name, $"\"{path}\"");
            }
        }
        catch (Exception)
        {
            // Autostart is a convenience; never fail the app start over it.
        }
    }

    private static bool SamePath(string? a, string b) => string.Equals(a?.Trim('"'), b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes every method, and Task Manager's "disabled" marks so choosing one again really turns it on.</summary>
    private static void RemoveAll()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.DeleteValue(Name, throwOnMissingValue: false);
        File.Delete(ShortcutPath);
        DeleteTask();
        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey + "Run", writable: true))
            approved?.DeleteValue(Name, throwOnMissingValue: false);
        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey + "StartupFolder", writable: true))
            approved?.DeleteValue(ShortcutName, throwOnMissingValue: false);
    }

    /// <summary>Task Manager's Startup apps switch: an odd first byte means disabled.</summary>
    private static bool Approved(string list, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey + list);
        return key?.GetValue(name) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
    }

    private static string? RunTarget()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) as string;
    }

    // Startup folder shortcut, through the shell's WScript.Shell object.

    private static void CreateShortcut(string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            var link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = "Desktop Widgets";
            link.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string? ShortcutTarget()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            return (string?)shell.CreateShortcut(ShortcutPath).TargetPath;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    // Task Scheduler, through its Schedule.Service object. A sign-in task for the current user only
    // can be created without administrator rights.

    private static T WithRootFolder<T>(Func<dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try
        {
            service.Connect();
            return action(service.GetFolder(@"\"));
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }

    private static dynamic? FindTask(dynamic folder)
    {
        try
        {
            return folder.GetTask(Name);
        }
        catch (Exception e) when (e.HResult == unchecked((int)0x80070002))
        {
            return null; // not found (reported as FileNotFoundException)
        }
    }

    private static string? TaskTarget() => WithRootFolder<string?>(folder =>
    {
        var task = FindTask(folder);
        if (task is null)
            return null;
        var actions = task.Definition.Actions;
        return actions.Count > 0 ? (string?)actions.Item(1).Path : null;
    });

    private static bool TaskEnabled() => WithRootFolder<bool>(folder => FindTask(folder) is { } task && (bool)task.Enabled);

    private static void DeleteTask() => WithRootFolder<bool>(folder =>
    {
        if (FindTask(folder) is not null)
            folder.DeleteTask(Name, 0);
        return true;
    });

    private static void CreateTask(string target)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(target);
        var dir = SecurityElement.Escape(Path.GetDirectoryName(target) ?? "");
        // A short delay lets the desktop finish loading; no time limit (the default would stop it after 3 days)
        // and no battery rules, so a laptop on battery still starts it.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Desktop Widgets when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command><WorkingDirectory>{dir}</WorkingDirectory></Exec></Actions>
            </Task>
            """;
        // 6 = create or update, 3 = interactive token (runs as the signed-in user, no password stored).
        WithRootFolder<bool>(folder =>
        {
            folder.RegisterTask(Name, xml, 6, null, null, 3);
            return true;
        });
    }
}
