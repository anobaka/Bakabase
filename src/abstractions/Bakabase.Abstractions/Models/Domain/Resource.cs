﻿using Bakabase.InsideWorld.Models.Constants;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Bakabase.InsideWorld.Models.Extensions;

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

    public int? ParentId { get; set; }
    public bool HasChildren { get; set; }
    public bool IsFile { get; set; }

    public DateTime CreateDt { get; set; } = DateTime.Now;
    public DateTime UpdateDt { get; set; } = DateTime.Now;
    public DateTime FileCreateDt { get; set; }
    public DateTime FileModifyDt { get; set; }

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