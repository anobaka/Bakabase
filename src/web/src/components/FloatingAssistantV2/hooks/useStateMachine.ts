import { useReducer, useCallback } from 'react';
import type { BakaState, BakaEvent, DockedEdge, IdlePose, HoverPose, ClickPose, PeekPose } from '../types';
import {
  HOVER_POSES,
  CLICK_POSES,
  PEEK_POSES,
} from '../constants';
import { loadPosition } from './useDraggable';

function randomFrom<T>(arr: readonly T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

interface MachineState {
  state: BakaState;
  previousState: BakaState;
  dockedEdge: DockedEdge;
  idlePose: IdlePose;
  hoverPose: HoverPose;
  clickPose: ClickPose;
  peekPose: PeekPose;
  /** true when temporarily undocked via hover — should redock on hover leave */
  undockedByHover: boolean;
}

function getInitialState(): MachineState {
  const saved = loadPosition();
  return {
    state: 'docked',
    previousState: 'docked',
    dockedEdge: saved.edge,
    idlePose: 'breathe',
    hoverPose: 'notice',
    clickPose: 'jump',
    peekPose: 'peek-wave',
    undockedByHover: false,
  };
}

function reducer(current: MachineState, event: BakaEvent): MachineState {
  const { state } = current;

  switch (event.type) {
    case 'DRAG_START':
      return { ...current, previousState: state, state: 'dragging', undockedByHover: false };

    case 'DRAG_END':
      if (event.nearEdge) {
        return { ...current, state: 'docked', dockedEdge: event.nearEdge };
      }
      return { ...current, state: 'floating-idle', idlePose: 'breathe' };

    case 'HOVER_ENTER':
      if (state === 'dragging' || state === 'chatting' || state === 'hovered') return current;
      if (state === 'waving-bye') {
        // Mouse came back during wave-bye — cancel and go back to hovered
        return { ...current, state: 'hovered', hoverPose: randomFrom(HOVER_POSES) };
      }
      if (state === 'docked' || state === 'peeking') {
        return {
          ...current,
          previousState: 'docked',
          state: 'hovered',
          hoverPose: randomFrom(HOVER_POSES),
          undockedByHover: true,
        };
      }
      return {
        ...current,
        previousState: state,
        state: 'hovered',
        hoverPose: randomFrom(HOVER_POSES),
        undockedByHover: false,
      };

    case 'HOVER_LEAVE':
      if (state !== 'hovered') return current;
      if (current.undockedByHover) {
        return { ...current, state: 'docked', undockedByHover: false };
      }
      return { ...current, state: current.previousState };

    case 'UNDOCK':
      // Triggered before auto-redock: play waving-bye animation
      if (state !== 'hovered') return current;
      return { ...current, state: 'waving-bye' };

    case 'REDOCK':
      // After waving-bye animation completes, go back to docked
      return { ...current, state: 'docked', undockedByHover: false };

    case 'CLICK':
      if (state === 'dragging') return current;
      return {
        ...current,
        previousState: state === 'hovered' ? current.previousState : state,
        state: 'clicked',
        clickPose: randomFrom(CLICK_POSES),
      };

    case 'CLICK_ANIM_DONE': {
      if (state !== 'clicked') return current;
      // If we were undocked by hover, stay as 'hovered' (character is still out)
      if (current.undockedByHover) {
        return { ...current, state: 'hovered' };
      }
      return { ...current, state: current.previousState };
    }

    case 'PEEK_START':
      if (state !== 'docked') return current;
      return {
        ...current,
        previousState: state,
        state: 'peeking',
        peekPose: randomFrom(PEEK_POSES),
      };

    case 'PEEK_END':
      if (state !== 'peeking') return current;
      return { ...current, state: 'docked' };

    case 'IDLE_POSE_CHANGE':
      if (state !== 'floating-idle') return current;
      return { ...current, idlePose: event.pose };

    case 'OPEN_CHAT':
      return { ...current, previousState: state, state: 'chatting' };

    case 'CLOSE_CHAT':
      if (current.undockedByHover) {
        return { ...current, state: 'hovered' };
      }
      return { ...current, state: current.previousState };

    default:
      return current;
  }
}

export function useStateMachine() {
  const [machine, dispatch] = useReducer(reducer, undefined, getInitialState);

  const send = useCallback((event: BakaEvent) => {
    dispatch(event);
  }, []);

  return { machine, send };
}
