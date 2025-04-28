using Bakabase.InsideWorld.Models.Constants;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Extensions;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record Resource
{
    public int Id { get; set; }

    public int MediaLibraryId { get; set; }
    public int CategoryId { get; set; }

    private string _fileName = null!;

    public string FileName
    {
        get
        {
            if (string.IsNullOrEmpty(_fileName))
            {
                if (!string.IsNullOrEmpty(_path))
                {
                    _fileName = System.IO.Path.GetFileName(_path);
                }
            }

            return _fileName;
        }
        set
        {
            _fileName = value;
            RebuildPath();
        }
    }

    private string _directory = null!;

    public string Directory
    {
        get
        {
            if (string.IsNullOrEmpty(_directory))
            {
                if (!string.IsNullOrEmpty(_path))
                {
                    _directory = System.IO.Path.GetDirectoryName(_path).StandardizePath()!;
                }
            }

            return _directory;
        }
        set
        {
            _directory = value;
            RebuildPath();
        }
    }

    private string _path = null!;

    public string Path
    {
        get
        {
            if (string.IsNullOrEmpty(_path))
            {
                RebuildPath();
            }

            return _path;
        }
        set
        {
            _path = value;
            if (!string.IsNullOrEmpty(value))
            {
                _directory = System.IO.Path.GetDirectoryName(value).StandardizePath()!;
                _fileName = System.IO.Path.GetFileName(value);
            }
        }
    }

    private string? _displayName;

    public string DisplayName
    {
        get => _displayName ?? FileName;
        set => _displayName = value;
    }

    public int? ParentId { get; set; }
    public bool HasChildren => Tags.Contains(ResourceTag.IsParent);
    public bool IsFile { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime FileCreatedAt { get; set; }
    public DateTime FileModifiedAt { get; set; }
    public List<string>? CoverPaths { get; set; }
    public HashSet<ResourceTag> Tags { get; set; } = [];
    public Resource? Parent { get; set; }

    /// <summary>
    /// ResourcePropertyType - PropertyId - Property
    /// </summary>
    public Dictionary<int, Dictionary<int, Property>>? Properties { get; set; }

    public bool Pinned => Tags.Contains(ResourceTag.Pinned);
    public DateTime? PlayedAt { get; set; }

    public ResourceCache? Cache { get; set; }

    public record Property(
        string? Name,
        PropertyType Type,
        StandardValueType DbValueType,
        StandardValueType BizValueType,
        List<Property.PropertyValue>? Values,
        bool Visible = false,
        int Order = 0)
    {
        public string? Name { get; set; } = Name;
        public List<PropertyValue>? Values { get; set; } = Values;
        public StandardValueType DbValueType { get; set; } = DbValueType;
        public StandardValueType BizValueType { get; set; } = BizValueType;
        public PropertyType Type { get; set; } = Type;
        public bool Visible { get; set; } = Visible;
        public int Order { get; set; } = Order;

        public record PropertyValue(
            int Scope,
            object? Value,
            object? BizValue,
            object? AliasAppliedBizValue)
        {
            public int Scope { get; set; } = Scope;
            public object? Value { get; set; } = Value;
            public object? BizValue { get; set; } = BizValue ?? Value;
            public object? AliasAppliedBizValue { get; set; } = AliasAppliedBizValue ?? BizValue ?? Value;
        }
    }

    public Category? Category { get; set; }

    public string? MediaLibraryName { get; set; }

    private void RebuildPath()
    {
        if (!string.IsNullOrEmpty(_fileName) || !string.IsNullOrEmpty(_directory))
        {
            if (string.IsNullOrEmpty(_fileName))
            {
                _path = _directory.StandardizePath()!;
            }
            else
            {
                if (string.IsNullOrEmpty(_directory))
                {
                    _path = _fileName;
                }
                else
                {
                    _path = System.IO.Path.Combine(_directory, _fileName).StandardizePath()!;
                }
            }
        }
        else
        {
            _path = null;
        }
    }
}