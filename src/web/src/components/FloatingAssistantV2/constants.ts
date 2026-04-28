import type { DockedEdge, IdlePose, HoverPose, ClickPose, PeekPose } from './types';

export const BAKA_SIZE = 52;
export const DOCKED_VISIBLE = 20; // px visible when docked at edge
export const EDGE_THRESHOLD = 50; // px from edge to snap
export const DRAG_THRESHOLD = 6;

export const POSITION_STORAGE_KEY = 'baka-assistant-v2-position';
export const STATE_STORAGE_KEY = 'baka-assistant-v2-state';

export const DEFAULT_DOCKED_EDGE: DockedEdge = 'left';

export const IDLE_POSES: IdlePose[] = ['breathe', 'blink', 'sway', 'yawn', 'look-around'];
export const HOVER_POSES: HoverPose[] = ['notice', 'smile', 'curious'];
export const CLICK_POSES: ClickPose[] = ['jump', 'spin', 'heart', 'wave'];
export const PEEK_POSES: PeekPose[] = ['peek-wave', 'peek-look'];

// Autonomous behavior timing (ms)
export const IDLE_POSE_INTERVAL_MIN = 6000;
export const IDLE_POSE_INTERVAL_MAX = 14000;
export const PEEK_INTERVAL_MIN = 30000;
export const PEEK_INTERVAL_MAX = 90000;
export const PEEK_DURATION = 4000;
export const CLICK_ANIM_DURATION = 800;
export const REDOCK_DELAY = 5000;       // 5s idle before auto-redock
export const WAVING_BYE_DURATION = 1200; // bye-bye wave animation length
