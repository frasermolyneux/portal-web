using System.Reflection;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Extensions;

public static class FileTransportCompatibilityExtensions
{
    public static bool GetFileTransportEnabled(this object source, bool fallbackFtpEnabled)
    {
        var value = source.GetOptionalBoolProperty("FileTransportEnabled");
        return value ?? fallbackFtpEnabled;
    }

    public static FileTransportType GetFileTransportType(this object source, bool fileTransportEnabled, bool fallbackFtpEnabled)
    {
        var value = source.GetOptionalEnumProperty("FileTransportType");
        if (value.HasValue && value.Value != FileTransportType.Unknown)
        {
            return value.Value;
        }

        // Backward compatibility inference when FileTransportType is missing:
        // - Legacy records only had FtpEnabled
        // - Newer records can have FileTransportEnabled=true with FtpEnabled=false, which implies SFTP
        if (!fileTransportEnabled && !fallbackFtpEnabled)
        {
            return FileTransportType.Unknown;
        }

        if (fallbackFtpEnabled)
        {
            return FileTransportType.Ftp;
        }

        return FileTransportType.Sftp;
    }

    public static void SetFileTransportProperties(this object target, bool fileTransportEnabled, FileTransportType fileTransportType)
    {
        SetOptionalBoolProperty(target, "FileTransportEnabled", fileTransportEnabled);
        SetOptionalEnumProperty(target, "FileTransportType", fileTransportType);
    }

    private static bool? GetOptionalBoolProperty(this object source, string propertyName)
    {
        var propertyInfo = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propertyInfo?.CanRead != true)
        {
            return null;
        }

        var value = propertyInfo.GetValue(source);
        if (value is bool b)
        {
            return b;
        }

        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static FileTransportType? GetOptionalEnumProperty(this object source, string propertyName)
    {
        var propertyInfo = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propertyInfo?.CanRead != true)
        {
            return null;
        }

        var value = propertyInfo.GetValue(source);
        if (value is null)
        {
            return null;
        }

        if (value is string s && Enum.TryParse<FileTransportType>(s, true, out var parsedFromString))
        {
            return parsedFromString;
        }

        if (value.GetType().IsEnum)
        {
            var name = Enum.GetName(value.GetType(), value);
            if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse<FileTransportType>(name, true, out var parsedFromEnumName))
            {
                return parsedFromEnumName;
            }

            var numericValue = Convert.ToInt32(value);
            if (Enum.IsDefined(typeof(FileTransportType), numericValue))
            {
                return (FileTransportType)numericValue;
            }
        }

        if (value is int intValue && Enum.IsDefined(typeof(FileTransportType), intValue))
        {
            return (FileTransportType)intValue;
        }

        return null;
    }

    private static void SetOptionalBoolProperty(object target, string propertyName, bool value)
    {
        var propertyInfo = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propertyInfo?.CanWrite != true)
        {
            return;
        }

        var targetType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        if (targetType == typeof(bool))
        {
            propertyInfo.SetValue(target, value);
        }
    }

    private static void SetOptionalEnumProperty(object target, string propertyName, FileTransportType value)
    {
        var propertyInfo = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propertyInfo?.CanWrite != true)
        {
            return;
        }

        var targetType = Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType;
        if (!targetType.IsEnum)
        {
            return;
        }

        var enumName = value.ToString();
        if (Enum.GetNames(targetType).Contains(enumName, StringComparer.OrdinalIgnoreCase))
        {
            var enumValue = Enum.Parse(targetType, enumName, ignoreCase: true);
            propertyInfo.SetValue(target, enumValue);
        }
    }
}
