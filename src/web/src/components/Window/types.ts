export interface WindowState {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  isMinimized: boolean;
  isMaximized: boolean;
  zIndex: number;
  innerWidth?: number;
  innerHeight?: number;
}

export interface WindowOptions {
  title?: string;
  initialPosition?: { x: number; y: number };
  initialSize?: { width: number; height: number };
  minWidth?: number;
  minHeight?: number;
  persistent?: boolean;
  renderHeaderActions?: () => React.ReactNode;
}

export interface WindowProps {
  title?: string;
  children: React.ReactNode;
  onClose?: () => void;
  afterClose?: () => void;
  onDestroyed?: () => void;
  windowOptions?: Omit<WindowOptions, 'persistent'>;
  renderHeaderActions?: () => React.ReactNode;
}
