"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import {
  Button,
  Checkbox,
  CheckboxGroup,
  Input,
  Modal,
  Textarea,
  toast,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PostParseTarget, postParseTargets } from "@/sdk/constants";

interface Props extends DestroyableProps {
  automaticallyParsing: boolean;
}

export function splitPostLinks(value: string) {
  return Array.from(
    new Set(
      value
        .split(/\r?\n/)
        .map((link) => link.trim())
        .filter(Boolean),
    ),
  );
}

const isHttpUrl = (value: string) => {
  try {
    return ["http:", "https:"].includes(new URL(value).protocol);
  } catch {
    return false;
  }
};

const AddTasksModal = ({ automaticallyParsing, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [mode, setMode] = useState<"links" | "text">("links");
  const [linksText, setLinksText] = useState("");
  const [text, setText] = useState("");
  const [title, setTitle] = useState("");
  const [targets, setTargets] = useState<PostParseTarget[]>([PostParseTarget.DownloadInfo]);
  const links = splitPostLinks(linksText);
  const invalidLinks = links.some((link) => !isHttpUrl(link));
  const valid =
    targets.length > 0 && (mode === "links" ? links.length > 0 && !invalidLinks : !!text.trim());

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>(
            automaticallyParsing ? "postParser.action.addAndParse" : "postParser.action.addTasks",
          ),
          isDisabled: !valid,
        },
      }}
      size="lg"
      title={t<string>("postParser.action.addTasks")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        if (!valid) throw new Error(t<string>("postParser.input.completeForm"));
        const input = {
          sourceLinksMap: {},
          targets,
          links: mode === "links" ? links : [],
          text: mode === "text" ? text : undefined,
          title: mode === "text" ? title.trim() || undefined : undefined,
        };
        const response = await BApi.postParser.addPostParserTasks(input);

        if (response.code)
          throw new Error(response.message || t<string>("postParser.result.failed"));
        toast.success(t<string>("postParser.result.added"));
      }}
    >
      <div className="flex flex-col gap-4">
        <div aria-label={t<string>("postParser.input.mode")} className="flex gap-2" role="group">
          {(["links", "text"] as const).map((value) => (
            <Button
              key={value}
              aria-pressed={mode === value}
              color={mode === value ? "primary" : "default"}
              size="sm"
              variant="flat"
              onPress={() => setMode(value)}
            >
              {t<string>(`postParser.input.${value}`)}
            </Button>
          ))}
        </div>
        {mode === "links" ? (
          <Textarea
            isRequired
            description={t<string>("postParser.input.linksHint")}
            errorMessage={invalidLinks ? t<string>("postParser.input.invalidLinks") : undefined}
            isInvalid={invalidLinks}
            label={t<string>("postParser.input.links")}
            minRows={6}
            placeholder={"https://example.com/posts/123\nhttps://example.com/posts/456"}
            value={linksText}
            onValueChange={setLinksText}
          />
        ) : (
          <>
            <Input
              label={t<string>("postParser.input.title")}
              value={title}
              onValueChange={setTitle}
            />
            <Textarea
              isRequired
              description={t<string>("postParser.input.textHint")}
              label={t<string>("postParser.input.text")}
              minRows={8}
              value={text}
              onValueChange={setText}
            />
          </>
        )}
        {postParseTargets.length > 1 && (
          <CheckboxGroup
            label={t<string>("postParser.label.selectTargets")}
            orientation="horizontal"
            value={targets.map(String)}
            onValueChange={(values) => setTargets(values.map(Number) as PostParseTarget[])}
          >
            {postParseTargets.map((target) => (
              <Checkbox key={target.value} value={String(target.value)}>
                {t<string>(`PostParseTarget.${target.label}`)}
              </Checkbox>
            ))}
          </CheckboxGroup>
        )}
        <p className="text-xs leading-relaxed text-default-500">
          {t<string>("postParser.tip.parseOnly")}
        </p>
      </div>
    </Modal>
  );
};

export default AddTasksModal;
