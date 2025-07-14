import React from 'react';
import { useTranslation } from 'react-i18next';
import { Chip, Tooltip } from '@/components/bakaui';

type Props = {
  className?: string;
  size?: 'sm' | 'md' | 'lg';
  variant?: 'solid' | 'bordered' | 'light' | 'flat' | 'faded' | 'shadow';
  color?: 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'danger';
  tooltipContent?: string;
  showTooltip?: boolean;
};

/**
 * BetaChip component for indicating beta features
 * 
 * @example
 * // Basic usage
 * <BetaChip />
 * 
 * @example
 * // Custom styling
 * <BetaChip size="md" color="primary" variant="solid" />
 * 
 * @example
 * // Custom tooltip
 * <BetaChip tooltipContent="This is a custom beta message" />
 * 
 * @example
 * // Without tooltip
 * <BetaChip showTooltip={false} />
 * 
 * @example
 * // Different colors for different beta types
 * <BetaChip color="warning" /> // Default beta warning
 * <BetaChip color="success" /> // Beta feature that's working well
 * <BetaChip color="danger" /> // Beta feature with known issues
 */
export default ({
  className,
  size = 'sm',
  variant = 'flat',
  color = 'warning',
  tooltipContent,
  showTooltip = true,
}: Props) => {
  const { t } = useTranslation();

  const defaultTooltipContent = (
    <>
      {t('This feature is in beta and may be unstable. Please report any issues you encounter.')}<br/>
      {t('BetaFeature.BackupWarning')}
    </>
  );

  const chip = (
    <Chip
      size={size}
      variant={variant}
      color={color}
      className={className}
    >
      {t('Beta')}
    </Chip>
  );

  if (!showTooltip) {
    return chip;
  }

  return (
    <Tooltip
      content={tooltipContent || defaultTooltipContent}
      color="foreground"
      placement="top"
    >
      {chip}
    </Tooltip>
  );
};
