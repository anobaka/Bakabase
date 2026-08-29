/// Client-side media classification by extension, used to pick a playback
/// surface (player / reader / none). A rough mirror of the server's
/// InternalOptions extension lists — only what the UI needs to route a tap.
library;

enum MediaKind { video, audio, image, other }

const _videoExtensions = {
  '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.ts',
  '.m2ts', '.mpg', '.mpeg', '.rm', '.rmvb', '.ogv', '.3gp', '.vob',
};

const _audioExtensions = {
  '.mp3', '.flac', '.aac', '.m4a', '.wav', '.ogg', '.opus', '.wma', '.ape',
  '.alac', '.aiff', '.dsf', '.dff',
};

const _imageExtensions = {
  '.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.avif', '.heic', '.jxl',
  '.tiff', '.tif',
};

/// The server addresses entries inside archives as `archive.zip!inner/path`.
const archiveEntrySeparator = '!';

MediaKind classifyPath(String path) {
  final dot = path.lastIndexOf('.');
  if (dot < 0) {
    return MediaKind.other;
  }
  final ext = path.substring(dot).toLowerCase();
  if (_videoExtensions.contains(ext)) {
    return MediaKind.video;
  }
  if (_audioExtensions.contains(ext)) {
    return MediaKind.audio;
  }
  if (_imageExtensions.contains(ext)) {
    return MediaKind.image;
  }
  return MediaKind.other;
}

bool isArchiveEntry(String path) => path.contains(archiveEntrySeparator);
