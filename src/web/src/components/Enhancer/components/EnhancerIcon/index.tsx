import { ThunderboltOutlined } from "@ant-design/icons";
import { BsRegex } from "react-icons/bs";
import { SiKodi } from "react-icons/si";
import { RiRobot2Line } from "react-icons/ri";
import { MdVideoLibrary, MdMovie } from "react-icons/md";

import { EnhancerId } from "@/sdk/constants";
import DLsite from "@/assets/logo/dlsite.png";
import Bangumi from "@/assets/logo/bangumi.png";
import ExHentai from "@/assets/logo/exhentai.png";
import Bakabase from "@/assets/logo/bakabase.png";

type Props = {
  id: EnhancerId;
};
const EnhancerIcon = ({ id }: Props) => {
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
    case EnhancerId.Tmdb:
      return <MdMovie className={"text-base"} />;
    case EnhancerId.Av:
      return <MdVideoLibrary className={"text-base"} />;
    case EnhancerId.AI:
      return <RiRobot2Line className={"text-base"} />;
  }

  return <ThunderboltOutlined className={"text-base"} />;
};

EnhancerIcon.displayName = "EnhancerIcon";

export default EnhancerIcon;
