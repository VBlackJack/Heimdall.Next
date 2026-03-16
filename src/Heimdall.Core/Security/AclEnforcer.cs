/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Heimdall.Core.Security;

/// <summary>
/// Enforces restrictive Windows ACLs on files and directories, limiting
/// access to the current user, local Administrators, and SYSTEM.
/// Fail-closed: throws on failure so callers cannot silently proceed
/// with permissive default ACLs on sensitive files (CWE-276 prevention).
/// </summary>
[SupportedOSPlatform("windows")]
public static class AclEnforcer
{
    /// <summary>
    /// Set a restrictive ACL on a file, limiting access to the current user,
    /// local Administrators, and SYSTEM. The DACL is protected (inheritance disabled)
    /// and all existing permissions are removed before applying the new rules.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when ACL cannot be applied.</exception>
    public static void SetFileAcl(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Cannot set ACL on non-existent file.", filePath);

        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var identity in GetRestrictedIdentities())
        {
            var rule = new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                AccessControlType.Allow);
            acl.AddAccessRule(rule);
        }

        var fileInfo = new FileInfo(filePath);
        fileInfo.SetAccessControl(acl);
    }

    /// <summary>
    /// Set a restrictive ACL on a directory, limiting access to the current user,
    /// local Administrators, and SYSTEM. Includes container and object inheritance
    /// so child items inherit the restricted permissions.
    /// </summary>
    /// <param name="directoryPath">Path to the directory.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directoryPath"/> is null or empty.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when ACL cannot be applied.</exception>
    public static void SetDirectoryAcl(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Cannot set ACL on non-existent directory: {directoryPath}");
        }

        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var identity in GetRestrictedIdentities())
        {
            var rule = new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);
            acl.AddAccessRule(rule);
        }

        var directoryInfo = new DirectoryInfo(directoryPath);
        directoryInfo.SetAccessControl(acl);
    }

    /// <summary>
    /// Check whether a file has the expected restrictive ACL
    /// (only current user, Administrators, SYSTEM).
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <returns>True if the file ACL matches the expected restrictive pattern.</returns>
    public static bool VerifyFileAcl(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var fileInfo = new FileInfo(filePath);
            var acl = fileInfo.GetAccessControl();

            // Check that inheritance is disabled (protected DACL)
            if (!acl.AreAccessRulesProtected)
                return false;

            var rules = acl.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                targetType: typeof(SecurityIdentifier));

            var expectedIdentities = GetRestrictedIdentities()
                .Select(id => id.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Each rule must be an Allow rule for one of the expected identities
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    return false;

                if (rule.IdentityReference is SecurityIdentifier sid
                    && !expectedIdentities.Contains(sid.Value))
                {
                    return false;
                }
            }

            return rules.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the list of security identifiers for the restricted ACL:
    /// current user, BUILTIN\Administrators, NT AUTHORITY\SYSTEM.
    /// </summary>
    /// <returns>Array of SecurityIdentifier instances.</returns>
    internal static SecurityIdentifier[] GetRestrictedIdentities()
    {
        var identities = new List<SecurityIdentifier>(3);

        var currentUser = WindowsIdentity.GetCurrent();
        if (currentUser.User is not null)
        {
            identities.Add(currentUser.User);
        }

        identities.Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        identities.Add(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));

        return identities.ToArray();
    }
}
