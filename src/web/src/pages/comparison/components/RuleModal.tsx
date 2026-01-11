"use client";

import type {
  BakabaseModulesComparisonModelsInputComparisonRuleInputModel as ComparisonRuleInputModel,
} from "@/sdk/Api";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";

import { Modal } from "@/components/bakaui";
import { PropertyPool, ComparisonMode, NullValueBehavior } from "@/sdk/constants";
import RuleConfigPanel, { getDefaultParameterForMode, validateRuleConfig } from "./RuleConfigPanel";
import type { RuleConfig, ModeParameter, RuleConfigValidationError } from "./RuleConfigPanel";

export interface RuleWithProperty extends ComparisonRuleInputModel {
  property?: IProperty;
  normalize?: boolean;
  parameter?: ModeParameter;
}

interface RuleModalProps {
  rule?: RuleWithProperty;
  isOpen: boolean;
  onClose: () => void;
  onSave: (rule: RuleWithProperty) => void;
}

const RuleModal = ({ rule, isOpen, onClose, onSave }: RuleModalProps) => {
  const { t } = useTranslation();
  const isEditing = !!rule?.property;

  const [config, setConfig] = useState<RuleConfig>({
    propertyPool: PropertyPool.Custom,
    propertyId: 0,
    mode: ComparisonMode.StrictEqual,
    parameter: null,
    normalize: false,
    weight: 1,
    isVeto: false,
    vetoThreshold: 1.0,
    oneNullBehavior: NullValueBehavior.Skip,
    bothNullBehavior: NullValueBehavior.Skip,
  });
  const [validationErrors, setValidationErrors] = useState<RuleConfigValidationError[]>([]);

  useEffect(() => {
    if (rule) {
      const mode = rule.mode as ComparisonMode;
      setConfig({
        propertyPool: rule.propertyPool,
        propertyId: rule.propertyId,
        property: rule.property,
        mode,
        parameter: rule.parameter ?? getDefaultParameterForMode(mode),
        normalize: rule.normalize ?? false,
        weight: rule.weight ?? 1,
        isVeto: rule.isVeto ?? false,
        vetoThreshold: rule.vetoThreshold ?? 1.0,
        oneNullBehavior: rule.oneNullBehavior as NullValueBehavior ?? NullValueBehavior.Skip,
        bothNullBehavior: rule.bothNullBehavior as NullValueBehavior ?? NullValueBehavior.Skip,
      });
    } else {
      setConfig({
        propertyPool: PropertyPool.Custom,
        propertyId: 0,
        mode: ComparisonMode.StrictEqual,
        parameter: null,
        normalize: false,
        weight: 1,
        isVeto: false,
        vetoThreshold: 1.0,
        oneNullBehavior: NullValueBehavior.Skip,
        bothNullBehavior: NullValueBehavior.Skip,
      });
    }
    setValidationErrors([]);
  }, [rule, isOpen]);

  const handleSave = async () => {
    const errors = validateRuleConfig(config, t);
    if (errors.length > 0) {
      setValidationErrors(errors);
      return;
    }

    const result: RuleWithProperty = {
      order: rule?.order ?? 0,
      propertyPool: config.property!.pool,
      propertyId: config.property!.id,
      property: config.property,
      mode: config.mode,
      parameter: config.parameter,
      normalize: config.normalize,
      weight: config.weight,
      isVeto: config.isVeto,
      vetoThreshold: config.vetoThreshold,
      oneNullBehavior: config.oneNullBehavior,
      bothNullBehavior: config.bothNullBehavior,
    };
    onSave(result);
    onClose();
  };

  return (
    <Modal
      size="lg"
      title={isEditing ? t("comparison.action.editRule") : t("comparison.action.addRule")}
      visible={isOpen}
      footer={{
        actions: ["ok", "cancel"],
      }}
      onClose={onClose}
      onOk={handleSave}
    >
      <RuleConfigPanel value={config} onChange={setConfig} validationErrors={validationErrors} />
    </Modal>
  );
};

export default RuleModal;
