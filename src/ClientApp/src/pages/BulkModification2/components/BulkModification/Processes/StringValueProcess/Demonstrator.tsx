import { Trans, useTranslation } from 'react-i18next';
import React from 'react';
import type { StringProcessOptions } from './models';
import type { IProperty } from '@/components/Property/models';
import { TextProcessingOperation } from '@/sdk/constants';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import {
  ProcessValueDemonstrator,
} from '@/pages/BulkModification2/components/BulkModification/ProcessValue';
import { buildLogger } from '@/components/utils';

type Props = {
  operation?: TextProcessingOperation;
  options?: StringProcessOptions;
  variables?: BulkModificationVariable[];
};

const log = buildLogger('TextProcessDemonstrator');

export default (props: Props) => {
  const {
    operation,
    options,
    variables,
  } = props;
  const { t } = useTranslation();

  log(props);

  switch (operation!) {
    case TextProcessingOperation.SetWithFixedValue: {
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.SetDirectly'}
          >
            <div className="primary" />
            with fixed value
          </Trans>
          <ProcessValueDemonstrator
            value={options?.value}
            variables={variables}
          />
        </>
      );
    }
    case TextProcessingOperation.AddToStart:
    case TextProcessingOperation.AddToEnd:
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.AddToStartOrEnd'}
          values={{
            direction: operation == TextProcessingOperation.AddToStart ? t('Position.Beginning') : t('Position.End'),
            value: options?.value,
          }}
        >
          Add
          <ProcessValueDemonstrator
            value={options?.value}
            variables={variables}
          />
          to
          <span className="primary">beginning or end</span>
        </Trans>
      );
    case TextProcessingOperation.AddToAnyPosition:
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.AddToAnyPosition'}
          values={{
            direction: options?.isPositioningDirectionReversed ? t('Position.End') : t('Position.Beginning'),
            position: options?.index,
            value: options?.value,
          }}
        >
          Add
          <ProcessValueDemonstrator
            value={options?.value}
            variables={variables}
          />
          to the
          <div className="secondary">{options?.index}</div>
          position from the
          <div className="primary">end</div>
        </Trans>
      );
    case TextProcessingOperation.RemoveFromStart:
    case TextProcessingOperation.RemoveFromEnd:
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.RemoveFromStartOrEnd'}
            values={{
              direction: operation == TextProcessingOperation.RemoveFromStart ? t('Position.Beginning') : t('Position.End'),
              count: options?.count,
            }}
          >
            Remove
            <span className="secondary">{options?.count}</span>
            characters from
            <span className="primary">beginning or end</span>
          </Trans>
        </>
      );
    case TextProcessingOperation.RemoveFromAnyPosition: {
      const texts = {
        direction: options?.isPositioningDirectionReversed ? t('Position.End') : t('Position.Beginning'),
        position: options?.index,
        count: options?.count,
        removeDirection: options?.isOperationDirectionReversed ? t('TextOperation.Backward') : t('TextOperation.Forward'),
      };
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.RemoveFromAnyPosition'}
            values={texts}
          >
            {/* delete 6 characters forward from the fifth character from the end */}
            Delete
            <span className="secondary">{texts.count}</span>
            characters
            <span className="primary">{texts.removeDirection}</span>
            the
            <span className={'secondary'}>{texts.position}</span>
            character from the
            <span className="primary">{texts.direction}</span>
          </Trans>
        </>
      );
    }
    case TextProcessingOperation.ReplaceFromStart:
    case TextProcessingOperation.ReplaceFromEnd: {
      const texts = {
        direction: operation == TextProcessingOperation.ReplaceFromEnd ? t('Position.End') : t('Position.Beginning'),
        find: options?.find,
      };
      return (
        <>
          <Trans
            i18nKey={'BulkModification.Processor.Demonstrator.Operation.ReplaceFromStartOrEnd'}
            values={texts}
          >
            {/* Replace xxx with yyy from start */}
            {/* 0 */}
            <div className="primary">Replace</div>
            {/* 1 */}
            <div className="secondary">{texts.find}</div>
            {/* 2 */}
            with
            {/* 3 */}
            <ProcessValueDemonstrator
              value={options?.value}
              variables={variables}
            />
            {/* 4 */}
            from
            {/* 5 */}
            <div className="primary">{texts.direction}</div>
          </Trans>
        </>
      );
    }
    case TextProcessingOperation.ReplaceFromAnyPosition: {
      const texts = {
        direction: options?.isPositioningDirectionReversed ? t('end') : t('start'),
        find: options?.find,
      };
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.Replace'}
          values={texts}
        >
          {/* Replace xxx with yyy */}
          {/* 0 */}
          <div className="primary">Replace</div>
          {/* 1 */}
          <div className="secondary">{texts.find}</div>
          {/* 2 */}
          with
          {/* 3 */}
          <ProcessValueDemonstrator
            value={options?.value}
            variables={variables}
          />
        </Trans>
      );
    }
    case TextProcessingOperation.ReplaceWithRegex: {
      return (
        <Trans
          i18nKey={'BulkModification.Processor.Demonstrator.Operation.ReplaceWithRegex'}
          values={{
            find: options?.find,
          }}
        >
          {/* Use regex to replace xxx with yyy */}
          {/* 0 */}
          Use
          {/* 1 */}
          <div className="primary">regex</div>
          {/* 2 */}
          to
          {/* 3 */}
          <div className="primary">replace</div>
          {/* 4 */}
          <div className="secondary">{options?.find}</div>
          {/* 5 */}
          with
          {/* 6 */}
          <ProcessValueDemonstrator
            value={options?.value}
            variables={variables}
          />
        </Trans>
      );
    }
    case TextProcessingOperation.Delete: {
      return (
        <div className={'primary'}>
          {t('Delete')}
        </div>
      );
    }
    default:
      return (
        <>
          {t('Unsupported value')}
        </>
      );
  }
};
