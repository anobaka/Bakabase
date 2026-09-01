namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// A step's stored configuration cannot be understood. Deliberately NOT subject to the
/// per-item Skip error policy: a config problem hits every item identically, and under Skip it
/// used to silently degrade the step to its default behavior — for a filter, "keep everything" —
/// right in front of whatever side effect came next (capability map §5·发现 5). The runner fails
/// the whole run on this exception so the user learns about the broken step instead.
/// </summary>
public class WorkflowActivityConfigException(string message, Exception? innerException = null)
    : Exception(message, innerException);
