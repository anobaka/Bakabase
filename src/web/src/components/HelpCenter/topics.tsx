import type { HelpTopicDefinition, HelpTopicId } from "./types";

import {
  AiOutlineApartment,
  AiOutlineCloudDownload,
  AiOutlineCluster,
  AiOutlineDatabase,
  AiOutlineEdit,
  AiOutlineInbox,
  AiOutlineNotification,
  AiOutlineProfile,
  AiOutlineRocket,
  AiOutlineSync,
  AiOutlineTags,
} from "react-icons/ai";

import BulkModificationTopic from "./topics/bulkModification";
import AcquisitionTopic from "./topics/acquisition";
import AcquisitionConceptDetail from "./topics/acquisition/ConceptDetail";
import { acquisitionConcepts } from "./topics/acquisition/concepts";
import CollectionTopic from "./topics/collection";
import CollectionConceptDetail from "./topics/collection/ConceptDetail";
import { collectionConcepts } from "./topics/collection/concepts";
import GettingStartedTopic from "./topics/gettingStarted";
import MultiDeviceTopic from "./topics/multiDevice";
import MultiDeviceConceptDetail from "./topics/multiDevice/ConceptDetail";
import { multiDeviceConcepts } from "./topics/multiDevice/concepts";
import PathMarkTopic from "./topics/pathMark";
import PathMarkConceptDetail from "./topics/pathMark/ConceptDetail";
import { pathMarkConcepts } from "./topics/pathMark/concepts";
import ResourceProfileTopic from "./topics/resourceProfile";
import UnmaterializedResourceTopic from "./topics/unmaterializedResource";
import SubscriptionTopic from "./topics/subscription";
import SubscriptionConceptDetail from "./topics/subscription/ConceptDetail";
import { subscriptionConcepts } from "./topics/subscription/concepts";
import WorkflowTopic from "./topics/workflow";
import WorkflowConceptDetail from "./topics/workflow/ConceptDetail";
import { workflowConcepts } from "./topics/workflow/concepts";

import NoticesTopic from "@/components/Notices/NoticesTopic";

/**
 * Registry of all help center topics. A guide joins the help center by adding an
 * entry here; nothing else needs to know it exists.
 *
 * Order is the reading order in the left navigation, so "getting started" leads —
 * it is also the topic the first-run help opens at.
 */
export const helpTopics: HelpTopicDefinition[] = [
  {
    id: "gettingStarted",
    titleKey: "helpCenter.topic.gettingStarted",
    icon: <AiOutlineRocket className="text-lg" />,
    Content: GettingStartedTopic,
  },
  {
    id: "pathMark",
    titleKey: "helpCenter.topic.pathMark",
    icon: <AiOutlineTags className="text-lg" />,
    Content: PathMarkTopic,
    conceptGroupLabelKey: "helpCenter.pathMark.section.concepts",
    concepts: pathMarkConcepts.map((concept) => ({
      id: concept.id,
      labelKey: `helpCenter.pathMark.concept.${concept.id}.name`,
    })),
    ConceptContent: PathMarkConceptDetail,
  },
  {
    id: "workflow",
    titleKey: "helpCenter.topic.workflow",
    icon: <AiOutlineApartment className="text-lg" />,
    Content: WorkflowTopic,
    conceptGroupLabelKey: "helpCenter.workflow.section.concepts",
    concepts: workflowConcepts.map((concept) => ({
      id: concept.id,
      labelKey: `helpCenter.workflow.concept.${concept.id}.name`,
    })),
    ConceptContent: WorkflowConceptDetail,
  },
  {
    id: "resourceProfile",
    titleKey: "helpCenter.topic.resourceProfile",
    icon: <AiOutlineProfile className="text-lg" />,
    Content: ResourceProfileTopic,
  },
  {
    id: "unmaterializedResource",
    titleKey: "helpCenter.topic.unmaterializedResource",
    icon: <AiOutlineInbox className="text-lg" />,
    Content: UnmaterializedResourceTopic,
  },
  {
    id: "bulkModification",
    titleKey: "helpCenter.topic.bulkModification",
    icon: <AiOutlineEdit className="text-lg" />,
    Content: BulkModificationTopic,
  },
  {
    id: "collection",
    titleKey: "helpCenter.topic.collection",
    icon: <AiOutlineDatabase className="text-lg" />,
    Content: CollectionTopic,
    conceptGroupLabelKey: "helpCenter.collection.section.concepts",
    concepts: collectionConcepts,
    ConceptContent: CollectionConceptDetail,
  },
  {
    id: "subscription",
    titleKey: "helpCenter.topic.subscription",
    icon: <AiOutlineSync className="text-lg" />,
    Content: SubscriptionTopic,
    conceptGroupLabelKey: "helpCenter.subscription.section.concepts",
    concepts: subscriptionConcepts,
    ConceptContent: SubscriptionConceptDetail,
  },
  {
    id: "acquisition",
    titleKey: "helpCenter.topic.acquisition",
    icon: <AiOutlineCloudDownload className="text-lg" />,
    Content: AcquisitionTopic,
    conceptGroupLabelKey: "helpCenter.acquisition.section.concepts",
    concepts: acquisitionConcepts,
    ConceptContent: AcquisitionConceptDetail,
  },
  {
    id: "multiDevice",
    titleKey: "helpCenter.topic.multiDevice",
    icon: <AiOutlineCluster className="text-lg" />,
    Content: MultiDeviceTopic,
    conceptGroupLabelKey: "helpCenter.multiDevice.section.concepts",
    concepts: multiDeviceConcepts,
    ConceptContent: MultiDeviceConceptDetail,
  },
  {
    // Every notice the app opened at startup, to read again after "Got it".
    id: "notices",
    titleKey: "helpCenter.topic.notices",
    icon: <AiOutlineNotification className="text-lg" />,
    Content: NoticesTopic,
  },
];

export const getHelpTopic = (id: HelpTopicId): HelpTopicDefinition =>
  helpTopics.find((topic) => topic.id === id) ?? helpTopics[0]!;
