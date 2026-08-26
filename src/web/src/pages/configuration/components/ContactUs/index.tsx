"use client";

import type { SettingItem } from "@/pages/configuration/components/SettingsSection";

import React from "react";
import { GithubOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import Urls from "@/cons/Urls";
import qqGroupImg from "@/assets/qq-group.png";
import { Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import SettingsSection from "@/pages/configuration/components/SettingsSection";

const contacts: SettingItem[] = [
  {
    id: "github",
    label: "Github",
    keywords: ["source", "issue", "repository", "仓库", "源码"],
    render: () => (
      <Button
        color={"default"}
        size={"sm"}
        onClick={() => {
          BApi.gui.openUrlInDefaultBrowser({ url: Urls.Github });
        }}
      >
        <GithubOutlined className={"text-lg"} />
        <span className={"font-bold"}>Github</span>
      </Button>
    ),
  },
  {
    id: "qq",
    label: "QQ",
    keywords: ["group", "chat", "群", "交流"],
    render: () => <img alt="QQ" className="max-w-[200px]" src={qqGroupImg} />,
  },
];

interface ContactUsProps {
  query?: string;
}

const ContactUs: React.FC<ContactUsProps> = ({ query }) => {
  const { t } = useTranslation();

  return (
    <SettingsSection
      items={contacts}
      keywords={["contact", "support", "community", "联系", "反馈"]}
      query={query}
      title={t<string>("configuration.contact.title")}
    />
  );
};

ContactUs.displayName = "ContactUs";

export default ContactUs;
