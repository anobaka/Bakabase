import React from 'react';
import type { DockedEdge } from '../types';
import { BAKA_SIZE } from '../constants';
import ChatView from './ChatView';

interface Props {
  visible: boolean;
  position: { x: number; y: number };
  dockedEdge: DockedEdge;
  onClose: () => void;
}

const PANEL_WIDTH = 360;
const PANEL_HEIGHT = 480;

const ChatPanel: React.FC<Props> = ({ visible, position, dockedEdge, onClose }) => {
  if (!visible) return null;

  const panelStyle = computePanelPosition(position, dockedEdge);

  return (
    <div className="baka-chat-panel" style={panelStyle} onClick={(e) => e.stopPropagation()}>
      <ChatView mode="popover" onClose={onClose} />
    </div>
  );
};

function computePanelPosition(pos: { x: number; y: number }, _edge: DockedEdge): React.CSSProperties {
  const vw = window.innerWidth;
  const vh = window.innerHeight;

  const spaceRight = vw - pos.x - BAKA_SIZE;
  const spaceLeft = pos.x;

  let left: number;
  if (spaceRight >= PANEL_WIDTH + 8) {
    left = pos.x + BAKA_SIZE + 8;
  } else if (spaceLeft >= PANEL_WIDTH + 8) {
    left = pos.x - PANEL_WIDTH - 8;
  } else {
    left = Math.max(8, (vw - PANEL_WIDTH) / 2);
  }

  const top = Math.max(8, Math.min(pos.y - 40, vh - PANEL_HEIGHT - 8));

  return {
    position: 'fixed',
    left,
    top,
    width: PANEL_WIDTH,
    height: PANEL_HEIGHT,
    zIndex: 50,
  };
}

export default React.memo(ChatPanel);
