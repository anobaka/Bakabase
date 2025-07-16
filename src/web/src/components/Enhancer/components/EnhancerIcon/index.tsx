import { ThunderboltOutlined } from "@ant-design/icons";
import { BsRegex } from "react-icons/bs";
import { SiKodi } from "react-icons/si";

import { EnhancerId } from "@/sdk/constants";
import DLsite from "@/assets/logo/dlsite.png";
import Bangumi from "@/assets/logo/bangumi.png";
import ExHentai from "@/assets/logo/exhentai.png";
import Bakabase from "@/assets/logo/bakabase.png";

type Props = {
  id: EnhancerId;
};

export default ({ id }: Props) => {
  switch (id) {
    case EnhancerId.Bakabase:
      return (
        <img alt={""} className={"max-h-[16px]"} height={16} src={Bakabase} />
      );
    case EnhancerId.ExHentai:
      return (
        <img alt={""} className={"max-h-[16px]"} height={16} src={ExHentai} />
      );
    case EnhancerId.Bangumi:
      return (
        <img alt={""} className={"max-h-[16px]"} height={16} src={Bangumi} />
      );
    case EnhancerId.DLsite:
      return (
        <img alt={""} className={"max-h-[16px]"} height={16} src={DLsite} />
      );
    case EnhancerId.Regex:
      return <BsRegex className={"text-base"} />;
    case EnhancerId.Kodi:
      return <SiKodi className={"text-base"} />;
  }

  return <ThunderboltOutlined className={"text-base"} />;
};
