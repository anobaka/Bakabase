# MediaPlayer Component

Enhanced media player with left-right layout, thumbnail panel, and support for various media types.

## Features

- **Left Panel (Thumbnail Panel)**
  - Collapsible panel with thumbnails
  - Active item highlighting
  - Page counter (current / total)
  - Vertical scrolling
  - Support for 2-level content (compressed files with nested files)
  - Visual indicators for compressed file contents

- **Right Panel (Content Panel)**
  - Vertical scrolling
  - Displays media vertically
  - Visual markers for 2nd level content (files inside compressed files)
  - Supports images, videos, audios, and text files

- **Preloading**
  - Automatically preloads next 3 items for smoother navigation

## Mock Data for Testing

The component includes mock data for testing purposes. To enable mock data:

### Option 1: URL Parameter
Add `?mockMediaPlayer=true` to your URL:
```
http://localhost:3000?mockMediaPlayer=true
```

### Option 2: LocalStorage
Open browser console and run:
```javascript
localStorage.setItem("mockMediaPlayer", "true");
```
Then refresh the page.

### Option 3: Code Constant
Edit `index.tsx` and change:
```typescript
const USE_MOCK_DATA = ... || false); // Change this to true
```
to:
```typescript
const USE_MOCK_DATA = ... || true); // Always use mock data
```

## Mock Data Structure

The mock data includes:
- Regular image files (jpg, png, gif)
- Video files with time segments
- Audio files (mp3, flac)
- Text files (txt, md)
- Compressed files (zip, rar, 7z) with nested content
- Mixed content for comprehensive testing

## Usage

```typescript
import MediaPlayer from "@/components/MediaPlayer";

// With real data
<MediaPlayer
  files={[
    { path: "path/to/image.jpg" },
    { path: "path/to/video.mp4", startTime: "00:00:00", endTime: "00:05:00" },
  ]}
  defaultActiveIndex={0}
  autoPlay={false}
/>

// Or use the static show method
MediaPlayer.show({
  files: [...],
  defaultActiveIndex: 0,
});
```

## File Structure

- `index.tsx` - Main component
- `index.scss` - Styles
- `components/ThumbnailPanel.tsx` - Thumbnail panel component
- `mockData.ts` - Mock data for testing
- `utils.tsx` - Utility functions



