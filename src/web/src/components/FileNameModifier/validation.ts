import type { BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation } from "@/sdk/Api";
import {
  FileNameModifierOperationType,
  FileNameModifierPosition,
} from "@/sdk/constants";

type Operation = BakabaseInsideWorldBusinessComponentsFileNameModifierModelsFileNameModifierOperation;

const OperationType = FileNameModifierOperationType;
const PositionType = FileNameModifierPosition;

export interface FieldRequirements {
  text?: boolean;
  targetText?: boolean;
  position?: boolean;
  positionIndex?: boolean;
  dateTimeFormat?: boolean;
  deleteCount?: boolean;
  deleteStartPosition?: boolean;
  caseType?: boolean;
  alphabetStartChar?: boolean;
  alphabetCount?: boolean;
}

/**
 * 根据操作类型和当前状态，返回每个字段是否必填
 */
export function getFieldRequirements(op: Operation): FieldRequirements {
  const requirements: FieldRequirements = {};

  switch (op.operation) {
    case OperationType.Insert:
      // text 始终必填
      requirements.text = true;
      // position 必填
      requirements.position = true;
      // 如果 position 是 BeforeText/AfterText，则 targetText 必填
      if (
        op.position === PositionType.BeforeText ||
        op.position === PositionType.AfterText
      ) {
        requirements.targetText = true;
      }
      // 如果 position 是 AtPosition，则 positionIndex 必填
      if (op.position === PositionType.AtPosition) {
        requirements.positionIndex = true;
      }
      break;

    case OperationType.Replace:
      // 如果 replaceEntire 为 true，则 targetText 不需要
      // 否则 targetText 必填（要替换什么）
      if (!op.replaceEntire) {
        requirements.targetText = true;
      }
      // text 可选（替换为什么，空表示删除匹配的文本）
      requirements.text = false;
      break;

    case OperationType.AddDateTime:
      // dateTimeFormat 必填
      requirements.dateTimeFormat = true;
      // position 必填
      requirements.position = true;
      // 如果 position 是 AtPosition，则 positionIndex 必填
      if (op.position === PositionType.AtPosition) {
        requirements.positionIndex = true;
      }
      // 如果 position 是 BeforeText/AfterText，则 targetText 必填
      if (
        op.position === PositionType.BeforeText ||
        op.position === PositionType.AfterText
      ) {
        requirements.targetText = true;
      }
      break;

    case OperationType.Delete:
      // deleteCount 和 deleteStartPosition 必填
      requirements.deleteCount = true;
      requirements.deleteStartPosition = true;
      // position 必填
      requirements.position = true;
      // targetText 可选（用于匹配特定文本后删除）
      requirements.targetText = false;
      // 如果 position 是 AtPosition，则 positionIndex 必填
      if (op.position === PositionType.AtPosition) {
        requirements.positionIndex = true;
      }
      break;

    case OperationType.ChangeCase:
      // caseType 必填
      requirements.caseType = true;
      break;

    case OperationType.AddAlphabetSequence:
      // alphabetStartChar 和 alphabetCount 必填
      requirements.alphabetStartChar = true;
      requirements.alphabetCount = true;
      // 如果 position 是 AtPosition，则 positionIndex 必填
      if (op.position === PositionType.AtPosition) {
        requirements.positionIndex = true;
      }
      break;

    case OperationType.Reverse:
      // 无需额外参数
      break;
  }

  return requirements;
}

/**
 * 校验操作，返回 i18n key（空字符串表示验证通过）
 */
export function validateOperation(op: Operation): string {
  if (!op.target) return "FileNameModifier.Error.TargetRequired";
  if (!op.operation) return "FileNameModifier.Error.OperationTypeRequired";

  const requirements = getFieldRequirements(op);

  // 检查必填字段
  if (requirements.text && !op.text) {
    return "FileNameModifier.Error.TextRequired";
  }
  if (requirements.targetText && !op.targetText) {
    return "FileNameModifier.Error.TargetTextRequired";
  }
  if (requirements.position && !op.position) {
    return "FileNameModifier.Error.PositionRequired";
  }
  if (requirements.positionIndex && op.positionIndex == null) {
    return "FileNameModifier.Error.PositionIndexRequired";
  }
  if (requirements.dateTimeFormat && !op.dateTimeFormat) {
    return "FileNameModifier.Error.DateTimeFormatRequired";
  }
  if (requirements.deleteCount && op.deleteCount == null) {
    return "FileNameModifier.Error.DeleteCountRequired";
  }
  if (requirements.deleteStartPosition && op.deleteStartPosition == null) {
    return "FileNameModifier.Error.DeleteStartPositionRequired";
  }
  if (requirements.caseType && !op.caseType) {
    return "FileNameModifier.Error.CaseTypeRequired";
  }
  if (requirements.alphabetStartChar && !op.alphabetStartChar) {
    return "FileNameModifier.Error.AlphabetStartCharRequired";
  }
  if (requirements.alphabetCount && op.alphabetCount == null) {
    return "FileNameModifier.Error.AlphabetCountRequired";
  }

  return "";
}
