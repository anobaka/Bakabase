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
    const existingWindows = this.getAllWindows();
    const baseX = initialState.x ?? 100;
    const baseY = initialState.y ?? 100;
    const offsetStep = 40; // Offset each new window by 40px diagonally

    // Always apply offset to avoid overlapping with existing windows
    const x = baseX + existingWindows.length * offsetStep;
    const y = baseY + existingWindows.length * offsetStep;

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
      this.currentMaxZIndex = Math.min(this.currentMaxZIndex + 1, this.maxZIndex);
      window.zIndex = this.currentMaxZIndex;
      this.notifyListeners();
    }
  }

  getNextZIndex(): number {
    this.currentMaxZIndex = Math.min(this.currentMaxZIndex + 1, this.maxZIndex);

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
