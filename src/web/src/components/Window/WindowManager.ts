import type { WindowState } from "./types";

// Global window manager for tracking all instances and magnetic snapping
export class WindowManager {
  private static instance: WindowManager;
  private windows: Map<string, WindowState> = new Map();
  private listeners: Set<(windows: WindowState[]) => void> = new Set();
  private snapThreshold = 20; // pixels
  private minZIndex = 1000;
  private maxZIndex = 2000;
  private currentMaxZIndex = 1000;

  static getInstance(): WindowManager {
    if (!WindowManager.instance) {
      WindowManager.instance = new WindowManager();
    }

    return WindowManager.instance;
  }

  registerWindow(id: string, initialState: Partial<WindowState>): WindowState {
    // Calculate offset position based on existing windows to avoid overlap
    const existingWindows = this.getOtherWindows(id);
    const baseX = initialState.x ?? 100;
    const baseY = initialState.y ?? 100;
    const offsetStep = 40; // Offset each new window by 40px diagonally

    // Reflect the cascade at viewport edges. Clamping every offset at the edge
    // would stack all subsequent windows over a remembered bottom-right position.
    const maxX = Math.max(
      0,
      (initialState.innerWidth ?? window.innerWidth) - (initialState.width ?? 800),
    );
    const maxY = Math.max(
      0,
      (initialState.innerHeight ?? window.innerHeight) - (initialState.height ?? 600),
    );
    const reflected = (base: number, offset: number, limit: number) => {
      if (!limit) return 0;
      const position = (Math.max(0, Math.min(base, limit)) + offset) % (2 * limit);

      return position > limit ? 2 * limit - position : position;
    };
    let x = reflected(baseX, existingWindows.length * offsetStep, maxX);
    let y = reflected(baseY, existingWindows.length * offsetStep, maxY);

    // Reflection can revisit a position. Try further offsets without ever moving
    // offscreen; a viewport with no spare room necessarily shares its position.
    for (let attempt = 1; attempt <= existingWindows.length; attempt++) {
      if (!existingWindows.some((other) => other.x === x && other.y === y)) break;
      const offset = (existingWindows.length + attempt) * offsetStep;

      x = reflected(baseX, offset, maxX);
      y = reflected(baseY, offset, maxY);
    }

    const state: WindowState = {
      id,
      width: initialState.width ?? 800,
      height: initialState.height ?? 600,
      isMinimized: initialState.isMinimized ?? false,
      isMaximized: initialState.isMaximized ?? false,
      zIndex: this.getNextZIndex(),
      ...initialState,
      // Override x and y with calculated offset positions to ensure they're not overwritten
      x,
      y,
    };

    this.windows.set(id, state);
    this.notifyListeners();

    return state;
  }

  unregisterWindow(id: string): void {
    this.windows.delete(id);
    this.notifyListeners();
  }

  updateWindow(id: string, updates: Partial<WindowState>): void {
    const window = this.windows.get(id);

    if (window) {
      Object.assign(window, updates);
      this.notifyListeners();
    }
  }

  getWindow(id: string): WindowState | undefined {
    return this.windows.get(id);
  }

  getAllWindows(): WindowState[] {
    return Array.from(this.windows.values());
  }

  getOtherWindows(excludeId: string): WindowState[] {
    return this.getAllWindows().filter((w) => w.id !== excludeId);
  }

  bringToFront(id: string): void {
    const window = this.windows.get(id);

    if (window) {
      window.zIndex = this.getNextZIndex();
      this.notifyListeners();
    }
  }

  getNextZIndex(): number {
    if (this.currentMaxZIndex >= this.maxZIndex) {
      // Keep stacking unique after long sessions; capping every focused window at
      // the same value would make more than one player believe it is active.
      const ordered = this.getAllWindows().sort((left, right) => left.zIndex - right.zIndex);

      ordered.forEach((state, index) => {
        state.zIndex = this.minZIndex + index;
      });
      this.currentMaxZIndex = this.minZIndex + ordered.length - 1;
    }
    this.currentMaxZIndex += 1;

    return this.currentMaxZIndex;
  }

  isTopmost(id: string): boolean {
    const window = this.windows.get(id);

    if (!window) return false;

    // Check if this window has the highest zIndex among all windows
    const allWindows = this.getAllWindows();
    const maxZIndex = Math.max(...allWindows.map((w) => w.zIndex));

    return window.zIndex === maxZIndex;
  }

  subscribe(listener: (windows: WindowState[]) => void): () => void {
    this.listeners.add(listener);

    return () => {
      this.listeners.delete(listener);
    };
  }

  private notifyListeners(): void {
    this.listeners.forEach((listener) => listener(this.getAllWindows()));
  }

  // Magnetic snapping logic
  snapPosition(
    id: string,
    x: number,
    y: number,
    width: number,
    height: number,
  ): { x: number; y: number } {
    const window = this.windows.get(id);

    if (!window) return { x, y };

    const otherWindows = this.getOtherWindows(id);
    const viewportWidth = window.innerWidth ?? document.documentElement.clientWidth;
    const viewportHeight = window.innerHeight ?? document.documentElement.clientHeight;

    let snappedX = x;
    let snappedY = y;

    // Snap to viewport edges
    if (Math.abs(x) < this.snapThreshold) {
      snappedX = 0;
    } else if (Math.abs(x + width - viewportWidth) < this.snapThreshold) {
      snappedX = viewportWidth - width;
    }

    if (Math.abs(y) < this.snapThreshold) {
      snappedY = 0;
    } else if (Math.abs(y + height - viewportHeight) < this.snapThreshold) {
      snappedY = viewportHeight - height;
    }

    // Snap to other windows
    for (const otherWindow of otherWindows) {
      if (otherWindow.isMinimized) continue;

      const otherX = otherWindow.x;
      const otherY = otherWindow.y;
      const otherWidth = otherWindow.width;
      const otherHeight = otherWindow.height;

      // Snap left edge to other window's right edge
      if (Math.abs(x - (otherX + otherWidth)) < this.snapThreshold) {
        snappedX = otherX + otherWidth;
      }
      // Snap right edge to other window's left edge
      else if (Math.abs(x + width - otherX) < this.snapThreshold) {
        snappedX = otherX - width;
      }
      // Snap left edge to other window's left edge
      else if (Math.abs(x - otherX) < this.snapThreshold) {
        snappedX = otherX;
      }
      // Snap right edge to other window's right edge
      else if (Math.abs(x + width - (otherX + otherWidth)) < this.snapThreshold) {
        snappedX = otherX + otherWidth - width;
      }

      // Snap top edge to other window's bottom edge
      if (Math.abs(y - (otherY + otherHeight)) < this.snapThreshold) {
        snappedY = otherY + otherHeight;
      }
      // Snap bottom edge to other window's top edge
      else if (Math.abs(y + height - otherY) < this.snapThreshold) {
        snappedY = otherY - height;
      }
      // Snap top edge to other window's top edge
      else if (Math.abs(y - otherY) < this.snapThreshold) {
        snappedY = otherY;
      }
      // Snap bottom edge to other window's bottom edge
      else if (Math.abs(y + height - (otherY + otherHeight)) < this.snapThreshold) {
        snappedY = otherY + otherHeight - height;
      }
    }

    return { x: snappedX, y: snappedY };
  }

  snapSize(
    id: string,
    x: number,
    y: number,
    width: number,
    height: number,
  ): { width: number; height: number } {
    const window = this.windows.get(id);

    if (!window) return { width, height };

    const otherWindows = this.getOtherWindows(id);
    const viewportWidth = window.innerWidth ?? document.documentElement.clientWidth;
    const viewportHeight = window.innerHeight ?? document.documentElement.clientHeight;

    let snappedWidth = width;
    let snappedHeight = height;

    // Snap to viewport edges
    if (Math.abs(x + width - viewportWidth) < this.snapThreshold) {
      snappedWidth = viewportWidth - x;
    }
    if (Math.abs(y + height - viewportHeight) < this.snapThreshold) {
      snappedHeight = viewportHeight - y;
    }

    // Snap to other windows
    for (const otherWindow of otherWindows) {
      if (otherWindow.isMinimized) continue;

      const otherX = otherWindow.x;
      const otherY = otherWindow.y;
      const otherWidth = otherWindow.width;
      const otherHeight = otherWindow.height;

      // Snap right edge to other window's left edge
      if (Math.abs(x + width - otherX) < this.snapThreshold) {
        snappedWidth = otherX - x;
      }
      // Snap right edge to other window's right edge
      else if (Math.abs(x + width - (otherX + otherWidth)) < this.snapThreshold) {
        snappedWidth = otherX + otherWidth - x;
      }

      // Snap bottom edge to other window's top edge
      if (Math.abs(y + height - otherY) < this.snapThreshold) {
        snappedHeight = otherY - y;
      }
      // Snap bottom edge to other window's bottom edge
      else if (Math.abs(y + height - (otherY + otherHeight)) < this.snapThreshold) {
        snappedHeight = otherY + otherHeight - y;
      }
    }

    return { width: snappedWidth, height: snappedHeight };
  }
}
