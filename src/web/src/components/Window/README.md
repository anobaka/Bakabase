# Window Component

A generic, reusable window system for creating windowed components with full window management capabilities.

## Features

- **Window Management**: Automatic z-index management, window focusing, and multi-window support
- **Window Controls**: Minimize, maximize, restore, and close operations
- **Magnetic Snapping**: Windows snap to viewport edges and other windows
- **Draggable & Resizable**: Full drag-and-drop and resize support
- **Persistent Across Routes**: Optional `persistent` flag to keep windows alive during navigation
- **Custom Header Actions**: Render custom buttons/actions in the window header

## Usage

### Basic Usage with `createWindow`

```tsx
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaPlayer from "@/components/MediaPlayer";

const MyComponent = () => {
  const { createWindow } = useBakabaseContext();

  const openPlayer = () => {
    createWindow(MediaPlayer, {
      entries: [...],
      defaultActiveIndex: 0,
    }, {
      title: "Media Player",
      persistent: true,
      initialSize: { width: 1000, height: 700 },
      initialPosition: { x: 100, y: 100 },
    });
  };

  return <button onClick={openPlayer}>Open Player</button>;
};
```

### Window Options

```typescript
interface WindowOptions {
  title?: string;                          // Window title
  initialPosition?: { x: number; y: number };  // Initial position
  initialSize?: { width: number; height: number };  // Initial size
  minWidth?: number;                       // Minimum width (default: 400)
  minHeight?: number;                      // Minimum height (default: 300)
  persistent?: boolean;                    // Keep alive across route changes
  renderHeaderActions?: () => React.ReactNode;  // Custom header buttons
}
```

### Custom Header Actions

```tsx
createWindow(MyComponent, { ...props }, {
  title: "My Window",
  renderHeaderActions: () => (
    <>
      <Button onClick={doSomething}>Action 1</Button>
      <Button onClick={doOtherThing}>Action 2</Button>
    </>
  ),
});
```

## Architecture

### Components

- **Window**: Main window component that wraps content
- **WindowManager**: Singleton class managing all windows
- **WindowHeader**: Window title bar with controls

### File Structure

```
src/components/Window/
├── Window.tsx           # Main window component
├── WindowManager.ts     # Window management singleton
├── WindowHeader.tsx     # Window header with controls
├── types.ts            # TypeScript types
├── styles.css          # Window styles
├── index.tsx           # Exports
└── README.md           # This file
```

## Migration from WindowedMediaPlayer

Old approach (deprecated):
```tsx
import WindowedMediaPlayer from "@/components/WindowedMediaPlayer";
createPortal(WindowedMediaPlayer, { ...props }, { persistent: true });
```

New approach:
```tsx
import MediaPlayer from "@/components/MediaPlayer";
createWindow(MediaPlayer, { ...props }, {
  title: "Player",
  persistent: true
});
```

## Benefits

1. **Separation of Concerns**: Window logic is separated from content logic
2. **Reusability**: Any component can be windowed
3. **Consistency**: All windows behave the same way
4. **Flexibility**: Easy to customize window behavior per instance
5. **Maintainability**: Single source of truth for window management
