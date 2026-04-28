import React, { useCallback, useEffect, useRef, useState } from 'react';
import './index.scss';
import { useStateMachine } from './hooks/useStateMachine';
import { useBakaDraggable } from './hooks/useDraggable';
import { useAutonomousBehavior } from './hooks/useAutonomousBehavior';
import BakaCharacter from './components/BakaCharacter';
import ChatPanel from './components/ChatPanel';
import TaskPopover from './components/TaskPopover';
import type { DockedEdge } from './types';
import { BAKA_SIZE, CLICK_ANIM_DURATION, REDOCK_DELAY, WAVING_BYE_DURATION } from './constants';
import { Popover } from '@/components/bakaui';

const HOVER_HITBOX_EXTRA = 24;

const FloatingAssistantV2: React.FC = () => {
  const { machine, send } = useStateMachine();
  const clickTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const mouseOverRef = useRef(false);
  const redockTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const wavingTimerRef = useRef<ReturnType<typeof setTimeout>>();
  const [popoverVisible, setPopoverVisible] = useState(false);

  const machineRef = useRef(machine);
  machineRef.current = machine;

  const onDragStart = useCallback(() => {
    send({ type: 'DRAG_START' });
  }, [send]);

  const onDragEnd = useCallback((nearEdge: DockedEdge | null) => {
    send({ type: 'DRAG_END', nearEdge });
  }, [send]);

  const { position, handleMouseDown, isDragging, isDraggingState, undockFromEdge, redockToEdge } =
    useBakaDraggable(onDragStart, onDragEnd);

  useAutonomousBehavior(machine.state, machine.idlePose, send);

  // ---- Auto-redock logic ----

  const cancelRedock = useCallback(() => {
    clearTimeout(redockTimerRef.current);
    clearTimeout(wavingTimerRef.current);
  }, []);

  const scheduleRedock = useCallback(() => {
    clearTimeout(redockTimerRef.current);
    clearTimeout(wavingTimerRef.current);
    redockTimerRef.current = setTimeout(() => {
      const m = machineRef.current;
      if (mouseOverRef.current) return;
      if (!m.undockedByHover) return;
      if (m.state === 'clicked' || m.state === 'chatting' || m.state === 'dragging') return;

      send({ type: 'UNDOCK' });

      wavingTimerRef.current = setTimeout(() => {
        if (mouseOverRef.current) {
          send({ type: 'HOVER_ENTER' });
          return;
        }
        const m2 = machineRef.current;
        redockToEdge(m2.dockedEdge);
        send({ type: 'REDOCK' });
      }, WAVING_BYE_DURATION);
    }, REDOCK_DELAY);
  }, [redockToEdge, send]);

  useEffect(() => {
    if (machine.state === 'hovered' && machine.undockedByHover && !mouseOverRef.current) {
      scheduleRedock();
    }
  }, [machine.state, machine.undockedByHover, scheduleRedock]);

  useEffect(() => {
    return () => {
      clearTimeout(redockTimerRef.current);
      clearTimeout(wavingTimerRef.current);
    };
  }, []);

  // ---- Event handlers ----

  const handleClick = useCallback(() => {
    if (isDragging()) return;

    if (machine.state === 'chatting') {
      send({ type: 'CLOSE_CHAT' });
      return;
    }
    if (machine.state === 'waving-bye') {
      cancelRedock();
      send({ type: 'HOVER_ENTER' });
      return;
    }

    // Single click → toggle task popover
    setPopoverVisible((v) => !v);
  }, [isDragging, machine.state, send, cancelRedock]);

  const handleDoubleClick = useCallback(() => {
    if (isDragging()) return;
    cancelRedock();
    clearTimeout(clickTimerRef.current);
    setPopoverVisible(false); // close popover if open
    send({ type: 'OPEN_CHAT' });
  }, [isDragging, send, cancelRedock]);

  const handleOpenChatFromPopover = useCallback(() => {
    setPopoverVisible(false);
    cancelRedock();
    send({ type: 'OPEN_CHAT' });
  }, [send, cancelRedock]);

  const handleMouseEnter = useCallback(() => {
    mouseOverRef.current = true;
    cancelRedock();
    if (isDragging()) return;
    if (machine.state === 'waving-bye') {
      send({ type: 'HOVER_ENTER' });
      return;
    }
    const wasDocked = machine.state === 'docked' || machine.state === 'peeking';
    send({ type: 'HOVER_ENTER' });
    if (wasDocked) {
      undockFromEdge(machine.dockedEdge);
    }
  }, [isDragging, machine.state, machine.dockedEdge, send, undockFromEdge, cancelRedock]);

  const handleMouseLeave = useCallback(() => {
    mouseOverRef.current = false;
    if (machine.state === 'clicked' || machine.state === 'chatting' || machine.state === 'waving-bye') return;
    if (machine.undockedByHover) {
      scheduleRedock();
    } else {
      send({ type: 'HOVER_LEAVE' });
    }
  }, [machine.state, machine.undockedByHover, send, scheduleRedock]);

  const handleCloseChat = useCallback(() => {
    send({ type: 'CLOSE_CHAT' });
  }, [send]);

  const cursorClass = isDraggingState ? 'cursor-grabbing' : 'cursor-grab';
  const isDocked = machine.state === 'docked' || machine.state === 'peeking';

  return (
    <>
      {isDocked && (
        <div
          className="baka-dock-hitbox"
          style={getDockHitboxStyle(machine.dockedEdge, position)}
          onMouseEnter={handleMouseEnter}
        />
      )}
      <Popover
        visible={popoverVisible}
        onOpenChange={(v) => {
          if (!v) setPopoverVisible(false);
        }}
        trigger={
          <div
            className={`baka-assistant-v2 baka-state-${machine.state} ${cursorClass}`}
            style={{
              left: position.x,
              top: position.y,
              width: BAKA_SIZE,
              height: BAKA_SIZE,
            }}
            onMouseDown={handleMouseDown}
            onClick={handleClick}
            onDoubleClick={handleDoubleClick}
            onMouseEnter={handleMouseEnter}
            onMouseLeave={handleMouseLeave}
          >
            <BakaCharacter
              state={machine.state}
              idlePose={machine.idlePose}
              hoverPose={machine.hoverPose}
              clickPose={machine.clickPose}
              peekPose={machine.peekPose}
              dockedEdge={machine.dockedEdge}
            />
          </div>
        }
      >
        <TaskPopover onOpenChat={handleOpenChatFromPopover} />
      </Popover>
      <ChatPanel
        visible={machine.state === 'chatting'}
        position={position}
        dockedEdge={machine.dockedEdge}
        onClose={handleCloseChat}
      />
    </>
  );
};

function getDockHitboxStyle(
  edge: DockedEdge,
  position: { x: number; y: number },
): React.CSSProperties {
  const size = BAKA_SIZE + HOVER_HITBOX_EXTRA * 2;
  const base: React.CSSProperties = {
    position: 'fixed',
    zIndex: 48,
  };
  switch (edge) {
    case 'left':
      return { ...base, left: 0, top: position.y - HOVER_HITBOX_EXTRA, width: BAKA_SIZE + HOVER_HITBOX_EXTRA, height: size };
    case 'right':
      return { ...base, right: 0, top: position.y - HOVER_HITBOX_EXTRA, width: BAKA_SIZE + HOVER_HITBOX_EXTRA, height: size };
    case 'top':
      return { ...base, top: 0, left: position.x - HOVER_HITBOX_EXTRA, width: size, height: BAKA_SIZE + HOVER_HITBOX_EXTRA };
    case 'bottom':
      return { ...base, bottom: 0, left: position.x - HOVER_HITBOX_EXTRA, width: size, height: BAKA_SIZE + HOVER_HITBOX_EXTRA };
  }
}

export default FloatingAssistantV2;
