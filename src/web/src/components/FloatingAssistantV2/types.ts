export type BakaState =
  | 'docked'
  | 'peeking'
  | 'floating-idle'
  | 'hovered'
  | 'clicked'
  | 'dragging'
  | 'chatting'
  | 'waving-bye';

export type DockedEdge = 'left' | 'right' | 'top' | 'bottom';

export type IdlePose = 'breathe' | 'blink' | 'sway' | 'yawn' | 'look-around';
export type HoverPose = 'notice' | 'smile' | 'curious';
export type ClickPose = 'jump' | 'spin' | 'heart' | 'wave';
export type PeekPose = 'peek-wave' | 'peek-look';

export type BakaEvent =
  | { type: 'HOVER_ENTER' }
  | { type: 'HOVER_LEAVE' }
  | { type: 'CLICK' }
  | { type: 'DRAG_START' }
  | { type: 'DRAG_END'; nearEdge: DockedEdge | null }
  | { type: 'PEEK_START' }
  | { type: 'PEEK_END' }
  | { type: 'IDLE_POSE_CHANGE'; pose: IdlePose }
  | { type: 'OPEN_CHAT' }
  | { type: 'CLOSE_CHAT' }
  | { type: 'CLICK_ANIM_DONE' }
  | { type: 'UNDOCK' }
  | { type: 'REDOCK' };

export interface BakaContext {
  state: BakaState;
  previousState: BakaState;
  dockedEdge: DockedEdge;
  idlePose: IdlePose;
  hoverPose: HoverPose;
  clickPose: ClickPose;
  peekPose: PeekPose;
  position: { x: number; y: number };
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: number;
}
