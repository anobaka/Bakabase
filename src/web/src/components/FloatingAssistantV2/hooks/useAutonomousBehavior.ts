import { useEffect, useRef } from 'react';
import type { BakaState, BakaEvent, IdlePose } from '../types';
import {
  IDLE_POSES,
  IDLE_POSE_INTERVAL_MIN,
  IDLE_POSE_INTERVAL_MAX,
  PEEK_INTERVAL_MIN,
  PEEK_INTERVAL_MAX,
  PEEK_DURATION,
} from '../constants';

function randomBetween(min: number, max: number): number {
  return min + Math.random() * (max - min);
}

function randomIdlePose(current: IdlePose): IdlePose {
  const others = IDLE_POSES.filter(p => p !== current);
  return others[Math.floor(Math.random() * others.length)];
}

export function useAutonomousBehavior(
  state: BakaState,
  currentIdlePose: IdlePose,
  send: (event: BakaEvent) => void,
) {
  const stateRef = useRef(state);
  const poseRef = useRef(currentIdlePose);

  useEffect(() => { stateRef.current = state; }, [state]);
  useEffect(() => { poseRef.current = currentIdlePose; }, [currentIdlePose]);

  // Idle pose cycling
  useEffect(() => {
    if (state !== 'floating-idle') return;

    let timer: ReturnType<typeof setTimeout>;

    const scheduleNext = () => {
      const delay = randomBetween(IDLE_POSE_INTERVAL_MIN, IDLE_POSE_INTERVAL_MAX);
      timer = setTimeout(() => {
        if (stateRef.current === 'floating-idle') {
          send({ type: 'IDLE_POSE_CHANGE', pose: randomIdlePose(poseRef.current) });
        }
        scheduleNext();
      }, delay);
    };

    scheduleNext();
    return () => clearTimeout(timer);
  }, [state, send]);

  // Peeking from docked state
  useEffect(() => {
    if (state !== 'docked') return;

    let timer: ReturnType<typeof setTimeout>;
    let peekTimer: ReturnType<typeof setTimeout>;

    const schedulePeek = () => {
      const delay = randomBetween(PEEK_INTERVAL_MIN, PEEK_INTERVAL_MAX);
      timer = setTimeout(() => {
        if (stateRef.current === 'docked') {
          send({ type: 'PEEK_START' });
          peekTimer = setTimeout(() => {
            if (stateRef.current === 'peeking') {
              send({ type: 'PEEK_END' });
            }
            schedulePeek();
          }, PEEK_DURATION);
        } else {
          schedulePeek();
        }
      }, delay);
    };

    schedulePeek();
    return () => {
      clearTimeout(timer);
      clearTimeout(peekTimer);
    };
  }, [state, send]);
}
