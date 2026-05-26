"use client";

import React, { useState } from "react";

import BakaCharacter from "@/components/FloatingAssistantV2/components/BakaCharacter";
import {
  CLICK_POSES,
  HOVER_POSES,
  IDLE_POSES,
  PEEK_POSES,
} from "@/components/FloatingAssistantV2/constants";
import type {
  BakaState,
  ClickPose,
  HoverPose,
  IdlePose,
  PeekPose,
} from "@/components/FloatingAssistantV2/types";
import "@/components/FloatingAssistantV2/index.scss";

interface CellProps {
  label: string;
  state: BakaState;
  idlePose?: IdlePose;
  hoverPose?: HoverPose;
  clickPose?: ClickPose;
  peekPose?: PeekPose;
  isWorking?: boolean;
  replayKey: number;
}

const Cell: React.FC<CellProps> = ({
  label,
  state,
  idlePose = "breathe",
  hoverPose = "notice",
  clickPose = "jump",
  peekPose = "peek-wave",
  isWorking = false,
  replayKey,
}) => {
  return (
    <div className="flex flex-col items-center gap-2 border rounded-lg p-3 border-default-200 w-[120px]">
      <div className="h-[80px] flex items-end justify-center w-full">
        <BakaCharacter
          key={replayKey}
          clickPose={clickPose}
          dockedEdge="left"
          hoverPose={hoverPose}
          idlePose={idlePose}
          isWorking={isWorking}
          peekPose={peekPose}
          state={state}
        />
      </div>
      <div className="text-xs text-center break-all leading-tight">{label}</div>
    </div>
  );
};

interface SectionProps {
  title: string;
  children: React.ReactNode;
}

const Section: React.FC<SectionProps> = ({ title, children }) => (
  <div className="flex flex-col gap-2">
    <h3 className="text-sm font-medium opacity-70">{title}</h3>
    <div className="flex flex-wrap gap-3">{children}</div>
  </div>
);

const BakaCharacterTest: React.FC = () => {
  const [replayKey, setReplayKey] = useState(0);

  return (
    <div className="flex flex-col gap-5 p-4">
      <div className="flex items-center gap-3">
        <button
          className="px-3 py-1.5 rounded-md bg-primary text-white text-sm"
          onClick={() => setReplayKey((k) => k + 1)}
        >
          Replay one-shot animations
        </button>
        <span className="text-xs opacity-60">
          (Looping animations play continuously; one-shot ones re-trigger on remount.)
        </span>
      </div>

      <Section title="floating-idle × IdlePose">
        {IDLE_POSES.map((pose) => (
          <Cell
            key={pose}
            idlePose={pose}
            label={`idle: ${pose}`}
            replayKey={replayKey}
            state="floating-idle"
          />
        ))}
      </Section>

      <Section title="hovered × HoverPose">
        {HOVER_POSES.map((pose) => (
          <Cell
            key={pose}
            hoverPose={pose}
            label={`hover: ${pose}`}
            replayKey={replayKey}
            state="hovered"
          />
        ))}
      </Section>

      <Section title="clicked × ClickPose">
        {CLICK_POSES.map((pose) => (
          <Cell
            key={pose}
            clickPose={pose}
            label={`click: ${pose}`}
            replayKey={replayKey}
            state="clicked"
          />
        ))}
      </Section>

      <Section title="peeking × PeekPose">
        {PEEK_POSES.map((pose) => (
          <Cell
            key={pose}
            label={`peek: ${pose}`}
            peekPose={pose}
            replayKey={replayKey}
            state="peeking"
          />
        ))}
      </Section>

      <Section title="Other top-level states">
        <Cell label="docked" replayKey={replayKey} state="docked" />
        <Cell label="dragging" replayKey={replayKey} state="dragging" />
        <Cell label="chatting" replayKey={replayKey} state="chatting" />
        <Cell label="waving-bye" replayKey={replayKey} state="waving-bye" />
      </Section>

      <Section title="working overlay (btasks running)">
        <Cell isWorking label="docked + working" replayKey={replayKey} state="docked" />
        <Cell
          isWorking
          idlePose="breathe"
          label="floating-idle + working"
          replayKey={replayKey}
          state="floating-idle"
        />
        <Cell
          isWorking
          hoverPose="smile"
          label="hovered + working (no overlay)"
          replayKey={replayKey}
          state="hovered"
        />
      </Section>
    </div>
  );
};

export default BakaCharacterTest;
