using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaptopQATestingMac.Services;

public static class ServiceNowService
{
    public static string BuildDescription(CachedWindowsSnapshot hardware)
    {
        var model = ModelNumber(hardware.Model);
        var serial = string.IsNullOrWhiteSpace(hardware.SerialNumber) ? "serial unavailable" : hardware.SerialNumber.Trim();
        var asset = string.IsNullOrWhiteSpace(hardware.AssetTag) ? "asset unavailable" : hardware.AssetTag.Trim();
        return $"Laptop QA | {model} | {serial} | {asset}";
    }

    private static string ModelNumber(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "model unavailable";
        var parts = Regex.Matches(model.Trim(), @"[A-Za-z]*\d+[A-Za-z0-9-]*")
            .Select(match => match.Value)
            .ToArray();
        return parts.Length == 0 ? model.Trim() : parts[^1];
    }

    public static string BuildAutofillUrl(AppConfig config, CachedWindowsSnapshot hardware)
    {
        var requestUrl = ValidateUrl(config.ServiceNowRequestUrl);
        var typeOfRequest = string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest.Trim();
        var prefill = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type_of_request"] = typeOfRequest,
            ["assignment_group"] = config.ServiceNowAssignmentGroupSysId?.Trim() ?? "",
            ["describe_request"] = BuildDescription(hardware)
        });
        return AddOrReplaceQueryParameter(requestUrl, "sysparm_variable_values", prefill);
    }

    private static string BuildLegacyAutofillScript(AppConfig config, CachedWindowsSnapshot hardware)
    {
        var description = JsonSerializer.Serialize(BuildDescription(hardware));
        var type = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest.Trim());
        var groupId = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupSysId?.Trim() ?? "");
        var groupName = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupName?.Trim() ?? "");
        return $$"""
(() => {
  const description = {{description}};
  const typeOfRequest = {{type}};
  const assignmentGroup = {{groupId}};
  const assignmentGroupDisplay = {{groupName}};

  function hideNativePrefillParameter() {
    try {
      const current = new URL(window.location.href);
      if (!current.searchParams.has("sysparm_variable_values")) return;
      current.searchParams.delete("sysparm_variable_values");
      const query = current.searchParams.toString();
      const clean = current.pathname + (query ? `?${query}` : "") + current.hash;
      window.history.replaceState(window.history.state, document.title, clean);
    } catch {}
  }

  function allDocuments(win = window, result = []) {
    try {
      result.push(win.document);
      for (const frame of win.frames) allDocuments(frame, result);
    } catch {}
    return result;
  }

  function setNativeValue(element, value) {
    if (!element) return false;
    try {
      const descriptor = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value");
      if (descriptor && descriptor.set) descriptor.set.call(element, value); else element.value = value;
      element.setAttribute("value", value);
      element.dispatchEvent(new Event("input", { bubbles: true }));
      element.dispatchEvent(new Event("change", { bubbles: true }));
      element.dispatchEvent(new Event("blur", { bubbles: true }));
      return true;
    } catch { return false; }
  }

  function glideFormFromScope(scope) {
    const visited = new Set();
    let current = scope;
    for (let depth = 0; current && depth < 20; depth++, current = current.$parent) {
      if (visited.has(current.$id)) break;
      visited.add(current.$id);
      try {
        if (current.page && typeof current.page.g_form === "function") {
          const form = current.page.g_form();
          if (form) return form;
        }
        if (current.page?.g_form && typeof current.page.g_form !== "function") return current.page.g_form;
        if (typeof current.getGlideForm === "function") {
          const form = current.getGlideForm();
          if (form) return form;
        }
        if (current.g_form) return current.g_form;
      } catch {}
    }
    return null;
  }

  function getGlideForm(doc, fieldElement = null) {
    const win = doc.defaultView;
    if (!win.angular) return win.g_form || null;
    const elements = [];
    let node = fieldElement;
    for (let depth = 0; node && depth < 15; depth++, node = node.parentElement) elements.push(node);
    for (const element of doc.querySelectorAll("[glide-form], [field], [ng-controller]")) {
      if (!elements.includes(element)) elements.push(element);
    }
    for (const element of elements) {
      try {
        const angularElement = win.angular.element(element);
        for (const scope of [angularElement.scope(), angularElement.isolateScope()].filter(Boolean)) {
          const form = glideFormFromScope(scope);
          if (form) return form;
        }
      } catch {}
    }
    if (win.g_form) return win.g_form;
    return null;
  }

  function updateAngularField(element, value, displayValue) {
    if (!element) return false;
    const win = element.ownerDocument.defaultView;
    if (!win.angular) return false;
    let updated = false;
    try {
      const visited = new Set();
      let node = element;
      for (let depth = 0; node && depth < 12; depth++, node = node.parentElement) {
        const angularElement = win.angular.element(node);
        try {
          const ngModel = angularElement.controller("ngModel");
          if (ngModel) {
            ngModel.$setViewValue(value);
            if (typeof ngModel.$commitViewValue === "function") ngModel.$commitViewValue();
            if (typeof ngModel.$setDirty === "function") ngModel.$setDirty();
            if (typeof ngModel.$setTouched === "function") ngModel.$setTouched();
            ngModel.$render();
            updated = true;
          }
        } catch {}
        for (const scope of [angularElement.scope(), angularElement.isolateScope()].filter(Boolean)) {
          if (!scope || visited.has(scope.$id)) continue;
          visited.add(scope.$id);
          for (const field of [scope.field, scope.page?.field].filter(Boolean)) {
            field.value = value;
            field.stagedValue = value;
            if (displayValue) {
              field.displayValue = displayValue;
              field.display_value = displayValue;
              field.displayValueStaged = displayValue;
            }
            if (field.reference) {
              field.reference.value = value;
              if (displayValue) field.reference.display_value = displayValue;
            }
            if (typeof scope.stagedValueChange === "function") scope.stagedValueChange();
            if (typeof scope.fieldValueChanged === "function") scope.fieldValueChanged();
            if (typeof scope.$evalAsync === "function") scope.$evalAsync();
            else if (typeof scope.$applyAsync === "function") scope.$applyAsync();
            updated = true;
          }
        }
      }
    } catch {}
    return updated;
  }

  function updateSelect2Display(doc, fieldId, displayText) {
    const chosen = doc.querySelector(`#s2id_${fieldId} .select2-chosen`);
    if (chosen) chosen.textContent = displayText;
    const container = doc.getElementById(`s2id_${fieldId}`);
    if (container) {
      container.classList.remove("select2-default");
      container.classList.add("select2-allowclear");
    }
  }

  function updateSelect2Value(doc, fieldId, value, displayText) {
    let ok = false;
    const element = doc.getElementById(fieldId);
    const win = doc.defaultView;
    const jq = win.jQuery || win.$;
    if (jq && element) {
      try { jq(element).val(value).trigger("input").trigger("change"); ok = true; } catch {}
      try { jq(element).select2("val", value); ok = true; } catch {}
      try { jq(element).select2("data", { id: value, text: displayText || value }); ok = true; } catch {}
      try { jq(element).trigger({ type: "select2:select", params: { data: { id: value, text: displayText || value } } }); ok = true; } catch {}
      try { jq(element).trigger("change"); ok = true; } catch {}
    }
    if (displayText) updateSelect2Display(doc, fieldId, displayText);
    return ok;
  }

  function setRelatedInputs(doc, fieldName, fieldId, value) {
    let ok = false;
    const selectors = [`#${fieldId}`, `[name="${fieldName}"]`, `[name="${fieldId}"]`, `input[id$="${fieldName}"]`, `textarea[id$="${fieldName}"]`];
    for (const selector of selectors) {
      for (const input of doc.querySelectorAll(selector)) {
        ok = setNativeValue(input, value) || ok;
        input.setAttribute("value", value);
      }
    }
    return ok;
  }

  function setSelectByTextOrValue(doc, id, text) {
    const candidates = [];
    const direct = doc.getElementById(id);
    if (direct?.options) candidates.push(direct);
    for (const select of doc.querySelectorAll(`select[id$="${id.replace("sp_formfield_", "")}"], select[name="${id}"], select[name="${id.replace("sp_formfield_", "")}"]`)) {
      if (!candidates.includes(select)) candidates.push(select);
    }
    let ok = false;
    for (const select of candidates) {
      const option = [...select.options].find(o => normalized(o.text) === normalized(text) || normalized(o.label) === normalized(text))
        || [...select.options].find(o => normalized(o.value) === normalized(text));
      if (!option) continue;
      const value = option.value;
      for (const item of select.options) item.selected = item === option;
      select.selectedIndex = option.index;
      try { select.focus(); } catch {}
      ok = setNativeValue(select, value) || ok;
      ok = updateAngularField(select, value, text) || ok;
      ok = updateSelect2Value(doc, select.id || id, value, text) || ok;
      try { if (typeof select.onchange === "function") select.onchange(); } catch {}
      try { select.blur(); } catch {}
      updateSelect2Display(doc, select.id || id, text);
    }
    return ok;
  }

  function resolveChoice(doc, fieldName, fieldId, displayText) {
    const wanted = normalized(displayText);
    if (!wanted) return null;
    const controls = [];
    const direct = doc.getElementById(fieldId);
    if (direct) controls.push(direct);
    for (const control of doc.querySelectorAll(
      `select[name="${fieldName}"], select[name="${fieldId}"], select[id$="${fieldName}"], [name="${fieldName}"]`
    )) {
      if (!controls.includes(control)) controls.push(control);
    }
    for (const label of doc.querySelectorAll("label")) {
      if (!normalized(label.textContent).includes("type of request")) continue;
      const target = label.htmlFor ? doc.getElementById(label.htmlFor) : null;
      if (target && !controls.includes(target)) controls.push(target);
      const nearby = label.parentElement?.querySelector("select, input, [role='combobox']");
      if (nearby && !controls.includes(nearby)) controls.push(nearby);
    }
    const form = getGlideForm(doc, direct);
    if (form && typeof form.getControl === "function") {
      try {
        const control = form.getControl(fieldName);
        if (control && !controls.includes(control)) controls.push(control);
      } catch {}
    }

    function chooseFromList(items, control, field = null, scope = null) {
      const choices = [...(items || [])];
      const labelOf = item => normalized(item?.label ?? item?.displayValue ?? item?.display_value ?? item?.text ?? item?.value);
      const valueOf = item => item?.value ?? item?.id ?? item?.name ?? "";
      const exact = choices.find(item => labelOf(item) === wanted || normalized(valueOf(item)) === wanted);
      const partial = choices.find(item => labelOf(item).startsWith(wanted + " ") || labelOf(item).includes(`(${wanted})`));
      const match = exact || partial;
      const value = valueOf(match);
      if (!match || value === null || value === undefined || String(value).trim() === "") return null;
      return {
        value: String(value),
        label: String(match.label ?? match.displayValue ?? match.display_value ?? match.text ?? displayText),
        control,
        field,
        scope,
        form: getGlideForm(doc, control)
      };
    }

    for (const control of controls) {
      const win = control.ownerDocument?.defaultView;
      if (win?.angular) {
        try {
          let node = control;
          for (let depth = 0; node && depth < 15; depth++, node = node.parentElement) {
            const angularElement = win.angular.element(node);
            for (const scope of [angularElement.scope(), angularElement.isolateScope()].filter(Boolean)) {
              for (const field of [scope?.field, scope?.page?.field].filter(Boolean)) {
                const choice = chooseFromList(field.choices || field.choiceList, control, field, scope);
                if (choice) return choice;
              }
            }
          }
        } catch {}
      }
      if (control.options) {
        const choice = chooseFromList(control.options, control);
        if (choice) return choice;
      }
    }
    for (const control of doc.querySelectorAll("select")) {
      if (!normalized(control.id).includes(fieldName) && !normalized(control.name).includes(fieldName)) continue;
      const choice = chooseFromList(control.options, control);
      if (choice) return choice;
    }
    return null;
  }

  function normalized(value) {
    return String(value ?? "").trim().toLowerCase();
  }

  function choiceFieldKeys(fieldName, fieldId, choice) {
    const field = choice?.field || {};
    const controlId = choice?.control?.id || "";
    const sysId = String(field.sys_id ?? field.sysId ?? "").trim();
    const candidates = [
      field.name,
      field.variable_name,
      field.variableName,
      field.id,
      controlId.startsWith("sp_formfield_") ? controlId.substring(13) : controlId,
      fieldId.startsWith("sp_formfield_") ? fieldId.substring(13) : fieldId,
      fieldName,
      sysId ? `IO:${sysId}` : "",
      sysId
    ].map(value => String(value ?? "").trim()).filter(Boolean);
    return [...new Set(candidates)];
  }

  function choiceControls(doc, fieldName, fieldId, choice) {
    const controls = [choice?.control, doc.getElementById(fieldId)];
    const field = choice?.field || {};
    const sysId = String(field.sys_id ?? field.sysId ?? "").trim();
    for (const key of choiceFieldKeys(fieldName, fieldId, choice)) {
      controls.push(doc.getElementById(key), doc.getElementById(`sp_formfield_${key}`), doc.getElementById(`sys_original.${key}`));
    }
    if (sysId) {
      controls.push(doc.getElementById(`IO:${sysId}`), doc.getElementById(`sp_formfield_IO:${sysId}`), doc.getElementById(`sys_original.IO:${sysId}`));
    }
    const wantedNames = new Set(choiceFieldKeys(fieldName, fieldId, choice));
    if (sysId) wantedNames.add(`IO:${sysId}`);
    for (const element of doc.querySelectorAll("[name]")) {
      if (wantedNames.has(String(element.getAttribute("name") || ""))) controls.push(element);
    }
    return [...new Set(controls.filter(Boolean))];
  }

  function catalogValueMatches(doc, fieldName, fieldId, value, displayValue) {
    const expected = [value, displayValue].map(normalized).filter(Boolean);
    if (!expected.length) return false;
    const values = [];
    const form = getGlideForm(doc);
    if (form && typeof form.getValue === "function") {
      try { values.push(form.getValue(fieldName)); } catch {}
      try { values.push(form.getDisplayValue(fieldName)); } catch {}
    }
    const selectors = [
      `#${fieldId}`,
      `[name="${fieldName}"]`,
      `[name="${fieldId}"]`,
      `input[id$="${fieldName}"]`,
      `textarea[id$="${fieldName}"]`,
      `select[id$="${fieldName}"]`,
      `#s2id_${fieldId} .select2-chosen`
    ];
    for (const selector of selectors) {
      for (const element of doc.querySelectorAll(selector)) {
        values.push(element.value, element.textContent, element.getAttribute("value"));
      }
    }
    return values.map(normalized).some(actual => expected.some(wanted => actual === wanted || actual.includes(wanted)));
  }

  function commitChoice(doc, fieldName, fieldId, choice) {
    if (!choice || !choice.value) return false;
    const value = choice.value;
    const displayValue = choice.label || value;
    let ok = false;
    const controls = choiceControls(doc, fieldName, fieldId, choice);
    for (const control of controls) {
      if (control.options) {
        const option = [...control.options].find(item => normalized(item.value) === normalized(value));
        if (option) {
          for (const item of control.options) item.selected = item === option;
          control.selectedIndex = option.index;
        }
      }
      ok = setNativeValue(control, value) || ok;
      ok = updateAngularField(control, value, displayValue) || ok;
      ok = updateSelect2Value(doc, control.id || fieldId, value, displayValue) || ok;
      try { control.dispatchEvent(new Event("change", { bubbles: true })); } catch {}
    }
    const form = choice.form || getGlideForm(doc, choice.control);
    if (form && typeof form.setValue === "function") {
      for (const key of choiceFieldKeys(fieldName, fieldId, choice)) {
        try { form.setValue(key, value); ok = true; } catch {}
      }
    }
    if (displayValue) updateSelect2Display(doc, fieldId, displayValue);
    return ok;
  }

  function choiceIsCommitted(doc, fieldName, fieldId, choice) {
    const expected = normalized(choice?.value);
    if (!expected) return false;
    const values = [];
    let invalid = false;
    const form = choice?.form || getGlideForm(doc, choice?.control);
    if (form && typeof form.getValue === "function") {
      for (const key of choiceFieldKeys(fieldName, fieldId, choice)) {
        try { values.push(form.getValue(key)); } catch {}
      }
    }
    const controls = choiceControls(doc, fieldName, fieldId, choice);
    for (const control of controls) {
      values.push(control.value, control.getAttribute?.("value"));
      if (control.validity?.valueMissing || control.getAttribute?.("aria-invalid") === "true") invalid = true;
      try {
        const win = control.ownerDocument.defaultView;
        let node = control;
        for (let depth = 0; node && depth < 15; depth++, node = node.parentElement) {
          const angularElement = win.angular?.element(node);
          const ngModel = angularElement?.controller("ngModel");
          values.push(ngModel?.$viewValue, ngModel?.$modelValue);
          if (ngModel?.$error?.required || ngModel?.$invalid === true) invalid = true;
          for (const scope of [angularElement?.scope(), angularElement?.isolateScope()].filter(Boolean)) {
            for (const field of [scope?.field, scope?.page?.field].filter(Boolean)) {
              values.push(field.value, field.stagedValue);
              if (field.isInvalid === true || field.invalid === true) invalid = true;
            }
          }
        }
      } catch {}
    }
    return !invalid && values.map(normalized).some(actual => actual === expected);
  }

  function setCatalogValue(doc, fieldName, fieldId, value, displayValue) {
    let ok = false;
    const element = doc.getElementById(fieldId);
    const form = getGlideForm(doc, element);
    if (form && typeof form.setValue === "function") {
      try {
        form.setValue(fieldName, value, displayValue || value);
        ok = true;
      } catch {
        try {
          form.setValue(fieldName, value);
          ok = true;
        } catch {}
      }
    }
    ok = setNativeValue(element, value) || ok;
    ok = setRelatedInputs(doc, fieldName, fieldId, value) || ok;
    ok = updateAngularField(element, value, displayValue) || ok;
    ok = updateSelect2Value(doc, fieldId, value, displayValue) || ok;
    if (displayValue) updateSelect2Display(doc, fieldId, displayValue);
    return ok;
  }

  function fillDocument(doc) {
    // A visible label is not enough for a mandatory ServiceNow choice variable. Resolve
    // the option's internal value from the loaded form, commit that value, then verify the
    // native control and Angular model are no longer required/invalid.
    const resolvedType = resolveChoice(doc, "type_of_request", "sp_formfield_type_of_request", typeOfRequest);
    const commitKey = normalized(resolvedType?.value);
    const previouslyCommitted = Boolean(commitKey) && doc.documentElement?.dataset?.laptopQaTypeCommit === commitKey;
    const typeSet = commitChoice(doc, "type_of_request", "sp_formfield_type_of_request", resolvedType);
    if (typeSet && commitKey && doc.documentElement?.dataset) doc.documentElement.dataset.laptopQaTypeCommit = commitKey;
    setCatalogValue(doc, "assignment_group", "sp_formfield_assignment_group", assignmentGroup, assignmentGroupDisplay);
    [
      "sp_formfield_describe_request",
      "sp_formfield_please_describe_your_request",
      "sp_formfield_description",
      "sp_formfield_short_description"
    ].some(id => {
      const element = doc.getElementById(id);
      return setNativeValue(element, description) || updateAngularField(element, description);
    }) || setRelatedInputs(doc, "describe_request", "sp_formfield_describe_request", description)
      || setRelatedInputs(doc, "please_describe_your_request", "sp_formfield_please_describe_your_request", description);

    const typeOk = Boolean(resolvedType) && previouslyCommitted && typeSet && choiceIsCommitted(doc, "type_of_request", "sp_formfield_type_of_request", resolvedType);
    const groupOk = catalogValueMatches(doc, "assignment_group", "sp_formfield_assignment_group", assignmentGroup, assignmentGroupDisplay);
    const descriptionOk = catalogValueMatches(doc, "describe_request", "sp_formfield_describe_request", description, description)
      || catalogValueMatches(doc, "please_describe_your_request", "sp_formfield_please_describe_your_request", description, description)
      || catalogValueMatches(doc, "description", "sp_formfield_description", description, description)
      || catalogValueMatches(doc, "short_description", "sp_formfield_short_description", description, description);
    return { typeOk, groupOk, descriptionOk };
  }

  function fill() {
    const merged = { typeOk: false, groupOk: false, descriptionOk: false };
    for (const doc of allDocuments()) {
      const result = fillDocument(doc);
      merged.typeOk = result.typeOk || merged.typeOk;
      merged.groupOk = result.groupOk || merged.groupOk;
      merged.descriptionOk = result.descriptionOk || merged.descriptionOk;
    }
    console.log("Laptop QA ServiceNow autofill", merged);
    return merged;
  }

  const result = fill();
  if (result.typeOk) hideNativePrefillParameter();
  return (result.typeOk && result.groupOk && result.descriptionOk ? "COMPLETE" : "WAIT")
    + `|type=${result.typeOk}|group=${result.groupOk}|description=${result.descriptionOk}`;
})()
""";
    }

    private static string BuildLegacySingleCommitAutofillScript(AppConfig config, CachedWindowsSnapshot hardware)
    {
        var description = JsonSerializer.Serialize(BuildDescription(hardware));
        var type = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest.Trim());
        var groupId = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupSysId?.Trim() ?? "");
        var groupName = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupName?.Trim() ?? "");
        return $$"""
(() => {
  const description = {{description}};
  const requestedType = {{type}};
  const assignmentGroup = {{groupId}};
  const assignmentGroupDisplay = {{groupName}};
  const stateName = "__laptopQaServiceNowCommit";

  const normalized = value => String(value ?? "").trim().toLowerCase();
  const fieldNames = field => [field?.name, field?.variable_name, field?.variableName, field?.id]
    .map(normalized).filter(Boolean);

  function allDocuments(win = window, result = []) {
    try {
      result.push(win.document);
      for (const frame of win.frames) allDocuments(frame, result);
    } catch {}
    return result;
  }

  function getForm(scope, doc) {
    const visited = new Set();
    for (let current = scope, depth = 0; current && depth < 20; current = current.$parent, depth++) {
      if (visited.has(current.$id)) break;
      visited.add(current.$id);
      try {
        if (current.page && typeof current.page.g_form === "function") {
          const form = current.page.g_form();
          if (form) return form;
        }
        if (typeof current.getGlideForm === "function") {
          const form = current.getGlideForm();
          if (form) return form;
        }
        if (current.g_form) return current.g_form;
      } catch {}
    }
    return doc.defaultView.g_form || null;
  }

  function fieldMatches(field, name, control) {
    const wanted = normalized(name);
    if (fieldNames(field).some(value => value === wanted || value.endsWith(`.${wanted}`))) return true;
    const sysId = normalized(field?.sys_id ?? field?.sysId);
    const controlId = normalized(control?.id).replace("sp_formfield_", "").replace("io:", "");
    return Boolean(sysId && controlId && controlId.includes(sysId));
  }

  function controlsFor(doc, name, id) {
    const result = [];
    const add = control => { if (control && !result.includes(control)) result.push(control); };
    add(doc.getElementById(id));
    for (const selector of [
      `[name="${name}"]`, `[name="${id}"]`, `[id$="${name}"]`,
      `select[name="${name}"]`, `textarea[name="${name}"]`, `input[name="${name}"]`
    ]) {
      try { for (const control of doc.querySelectorAll(selector)) add(control); } catch {}
    }
    for (const label of doc.querySelectorAll("label")) {
      if (!normalized(label.textContent).includes(normalized(name.replaceAll("_", " ")))) continue;
      add(label.htmlFor ? doc.getElementById(label.htmlFor) : null);
      add(label.parentElement?.querySelector("select, textarea, input, [role='combobox']"));
    }
    return result;
  }

  function contextFor(doc, name, id) {
    const win = doc.defaultView;
    const controls = controlsFor(doc, name, id);
    for (const control of controls) {
      let fallback = null;
      for (let node = control, depth = 0; node && depth < 14; node = node.parentElement, depth++) {
        try {
          const angularElement = win.angular?.element(node);
          const scopes = [angularElement?.scope(), angularElement?.isolateScope()].filter(Boolean);
          for (const scope of scopes) {
            for (const field of [scope.field, scope.page?.field].filter(Boolean)) {
              const context = { doc, control, scope, field, form: getForm(scope, doc) };
              if (fieldMatches(field, name, control)) return context;
              if (!fallback && depth < 4) fallback = context;
            }
          }
        } catch {}
      }
      if (fallback) return fallback;
      return { doc, control, scope: null, field: null, form: getForm(null, doc) };
    }
    return null;
  }

  function choiceFor(context, displayText) {
    const wanted = normalized(displayText);
    const choices = [...(context?.field?.choices || context?.field?.choiceList || [])];
    for (const choice of choices) {
      const label = String(choice?.label ?? choice?.displayValue ?? choice?.display_value ?? choice?.text ?? choice?.value ?? "");
      const value = String(choice?.value ?? choice?.id ?? "");
      if (normalized(label) === wanted || normalized(value) === wanted) return { value, label: label || displayText };
    }
    if (context?.control?.options) {
      for (const option of context.control.options) {
        if (normalized(option.text) === wanted || normalized(option.label) === wanted || normalized(option.value) === wanted)
          return { value: String(option.value), label: String(option.text || option.label || displayText) };
      }
    }
    return null;
  }

  function setNativeValue(control, value) {
    if (!control) return false;
    try {
      if (control.options) {
        const option = [...control.options].find(item => normalized(item.value) === normalized(value));
        if (option) {
          for (const item of control.options) item.selected = item === option;
          control.selectedIndex = option.index;
        }
      }
      const descriptor = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(control), "value");
      if (descriptor?.set) descriptor.set.call(control, value); else control.value = value;
      const EventType = control.ownerDocument.defaultView.Event;
      control.dispatchEvent(new EventType("input", { bubbles: true }));
      control.dispatchEvent(new EventType("change", { bubbles: true }));
      return true;
    } catch { return false; }
  }

  function setExactField(context, name, value, displayValue) {
    if (!context || value === null || value === undefined || String(value) === "") return false;
    let changed = false;
    if (context.form && typeof context.form.setValue === "function") {
      try { context.form.setValue(name, value, displayValue || value); changed = true; }
      catch { try { context.form.setValue(name, value); changed = true; } catch {} }
    }
    if (context.field && fieldMatches(context.field, name, context.control)) {
      context.field.value = value;
      context.field.stagedValue = value;
      if (displayValue) {
        context.field.displayValue = displayValue;
        context.field.display_value = displayValue;
        context.field.displayValueStaged = displayValue;
      }
      try { if (typeof context.scope?.stagedValueChange === "function") context.scope.stagedValueChange(); } catch {}
      try { if (typeof context.scope?.fieldValueChanged === "function") context.scope.fieldValueChanged(); } catch {}
      try { context.scope?.$evalAsync(); } catch {}
      changed = true;
    }
    if (context.control) {
      try {
        let node = context.control;
        for (let depth = 0; node && depth < 5; node = node.parentElement, depth++) {
          const controller = context.doc.defaultView.angular?.element(node)?.controller("ngModel");
          if (!controller) continue;
          controller.$setViewValue(value);
          if (typeof controller.$commitViewValue === "function") controller.$commitViewValue();
          changed = true;
          break;
        }
      } catch {}
      changed = setNativeValue(context.control, value) || changed;
    }
    return changed;
  }

  function actualValues(context, name) {
    const values = [];
    if (!context) return values;
    if (context.form && typeof context.form.getValue === "function") {
      try { values.push(context.form.getValue(name)); } catch {}
      try { values.push(context.form.getDisplayValue(name)); } catch {}
    }
    values.push(context.field?.value, context.field?.stagedValue, context.field?.displayValue,
      context.control?.value, context.control?.selectedOptions?.[0]?.text);
    try {
      let node = context.control;
      for (let depth = 0; node && depth < 5; node = node.parentElement, depth++) {
        const controller = context.doc.defaultView.angular?.element(node)?.controller("ngModel");
        if (controller) values.push(controller.$viewValue, controller.$modelValue);
      }
    } catch {}
    return values.map(value => String(value ?? "")).filter(Boolean);
  }

  function matches(context, name, expected, displayValue = "") {
    const wanted = [expected, displayValue].map(normalized).filter(Boolean);
    return actualValues(context, name).map(normalized).some(value => wanted.includes(value));
  }

  function clickChoice(context, displayText) {
    if (!context?.control) return false;
    try { context.control.click(); } catch {}
    for (const option of context.doc.querySelectorAll("[role='option'], .select2-result-label, option")) {
      if (normalized(option.textContent) !== normalized(displayText)) continue;
      try { option.click(); return true; } catch {}
    }
    return false;
  }

  let target = null;
  for (const doc of allDocuments()) {
    const typeContext = contextFor(doc, "type_of_request", "sp_formfield_type_of_request");
    if (typeContext) { target = { doc, typeContext }; break; }
  }
  if (!target) return "WAIT|form=loading";

  const typeChoice = choiceFor(target.typeContext, requestedType);
  if (!typeChoice?.value) return "WAIT|type-choice=loading";
  const groupContext = contextFor(target.doc, "assignment_group", "sp_formfield_assignment_group");
  const descriptionContext = contextFor(target.doc, "describe_request", "sp_formfield_describe_request")
    || contextFor(target.doc, "please_describe_your_request", "sp_formfield_please_describe_your_request")
    || contextFor(target.doc, "description", "sp_formfield_description");

  let state = window[stateName];
  if (!state) {
    setExactField(target.typeContext, "type_of_request", typeChoice.value, typeChoice.label);
    setExactField(groupContext, "assignment_group", assignmentGroup, assignmentGroupDisplay);
    if (descriptionContext) {
      const descriptionName = fieldNames(descriptionContext.field)[0]
        || (normalized(descriptionContext.control?.id).includes("please_describe") ? "please_describe_your_request" : "describe_request");
      setExactField(descriptionContext, descriptionName, description, description);
    }
    state = window[stateName] = { started: Date.now(), typeValue: typeChoice.value, menuOpened: false, optionClicked: false };
    return `COMMITTED|type-value=${typeChoice.value}|group=${Boolean(groupContext)}|description=${Boolean(descriptionContext)}`;
  }

  const typeOk = matches(target.typeContext, "type_of_request", state.typeValue, typeChoice.label);
  const groupOk = !assignmentGroup || matches(groupContext, "assignment_group", assignmentGroup, assignmentGroupDisplay);
  const descriptionNames = ["describe_request", "please_describe_your_request", "description"];
  const descriptionOk = Boolean(descriptionContext) && descriptionNames.some(name => matches(descriptionContext, name, description, description));
  if (typeOk && groupOk && descriptionOk) return `COMPLETE|type=${state.typeValue}|group=true|description=true`;

  const elapsed = Date.now() - state.started;
  if (!typeOk && elapsed > 1200 && !state.menuOpened) {
    try { target.typeContext.control?.click(); } catch {}
    state.menuOpened = true;
    return `SETTLING|type=false|group=${groupOk}|description=${descriptionOk}|menu=open`;
  }
  if (!typeOk && state.menuOpened && !state.optionClicked) {
    state.optionClicked = clickChoice(target.typeContext, typeChoice.label);
    return `SETTLING|type=false|group=${groupOk}|description=${descriptionOk}|option=${state.optionClicked}`;
  }
  if (elapsed > 8000) return `STOP|type=${typeOk}|group=${groupOk}|description=${descriptionOk}|resolved=${state.typeValue}`;
  return `SETTLING|type=${typeOk}|group=${groupOk}|description=${descriptionOk}`;
})()
""";
    }

    public static string BuildAutofillScript(AppConfig config, CachedWindowsSnapshot hardware)
    {
        var description = JsonSerializer.Serialize(BuildDescription(hardware));
        var type = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest.Trim());
        var groupId = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupSysId?.Trim() ?? "");
        var groupName = JsonSerializer.Serialize(config.ServiceNowAssignmentGroupName?.Trim() ?? "");
        return $$"""
(() => {
  const expectedType = {{type}};
  const expectedGroup = {{groupId}};
  const expectedGroupDisplay = {{groupName}};
  const expectedDescription = {{description}};
  const normalized = value => String(value ?? "").trim().toLowerCase();

  function allDocuments(win = window, result = []) {
    try {
      result.push(win.document);
      for (const frame of win.frames) allDocuments(frame, result);
    } catch {}
    return result;
  }

  function inspect(doc, name, id) {
    const model = [];
    const visible = [];
    let invalid = false;
    const controls = [];
    const add = control => { if (control && !controls.includes(control)) controls.push(control); };
    add(doc.getElementById(id));
    try { for (const control of doc.querySelectorAll(`[name="${name}"], [id$="${name}"]`)) add(control); } catch {}
    const display = doc.querySelector(`#s2id_${id} .select2-chosen`);
    if (display) visible.push(display.textContent);

    for (const control of controls) {
      visible.push(control.value, control.textContent, control.selectedOptions?.[0]?.text);
      if (control.validity?.valueMissing || control.getAttribute?.("aria-invalid") === "true") invalid = true;
      try {
        let node = control;
        for (let depth = 0; node && depth < 15; node = node.parentElement, depth++) {
          const angularElement = doc.defaultView.angular?.element(node);
          const ngModel = angularElement?.controller("ngModel");
          if (ngModel) {
            model.push(ngModel.$modelValue, ngModel.$viewValue);
            if (ngModel.$invalid === true || ngModel.$error?.required) invalid = true;
          }
          for (const scope of [angularElement?.scope(), angularElement?.isolateScope()].filter(Boolean)) {
            const field = scope.field || scope.page?.field;
            const fieldName = normalized(field?.name ?? field?.variable_name ?? field?.variableName);
            if (field && (fieldName === normalized(name) || normalized(control.id).includes(normalized(field?.sys_id ?? field?.sysId)))) {
              model.push(field.value, field.stagedValue, field.displayValue, field.display_value);
              if (field.isInvalid === true || field.invalid === true) invalid = true;
            }
            let form = null;
            try { if (scope.page && typeof scope.page.g_form === "function") form = scope.page.g_form(); } catch {}
            try { if (!form && typeof scope.getGlideForm === "function") form = scope.getGlideForm(); } catch {}
            if (form && typeof form.getValue === "function") {
              try { model.push(form.getValue(name)); } catch {}
              try { model.push(form.getDisplayValue(name)); } catch {}
            }
          }
        }
      } catch {}
    }
    return {
      found: controls.length > 0,
      invalid,
      model: model.map(value => String(value ?? "")).filter(Boolean),
      visible: visible.map(value => String(value ?? "")).filter(Boolean)
    };
  }

  function committed(result, expected, displayValue = "") {
    if (!result.found || result.invalid) return false;
    const wanted = [expected, displayValue].map(normalized).filter(Boolean);
    const model = result.model.map(normalized);
    const visible = result.visible.map(normalized);
    return model.length ? model.some(value => wanted.includes(value)) : visible.some(value => wanted.includes(value));
  }

  function summary(result) {
    return encodeURIComponent([...result.model, ...result.visible].filter(Boolean).slice(0, 5).join(" / ").slice(0, 180));
  }

  for (const doc of allDocuments()) {
    const type = inspect(doc, "type_of_request", "sp_formfield_type_of_request");
    if (!type.found) continue;
    const group = inspect(doc, "assignment_group", "sp_formfield_assignment_group");
    const description = inspect(doc, "describe_request", "sp_formfield_describe_request");
    const typeOk = committed(type, expectedType, expectedType);
    const groupOk = !expectedGroup || committed(group, expectedGroup, expectedGroupDisplay);
    const descriptionOk = committed(description, expectedDescription, expectedDescription);
    return `${typeOk && groupOk && descriptionOk ? "COMPLETE" : "WAIT"}|type=${typeOk}|group=${groupOk}|description=${descriptionOk}`
      + `|type-values=${summary(type)}|description-values=${summary(description)}`;
  }
  return "WAIT|form=loading";
})()
""";
    }

    private static string BuildFocusScript(string? choiceText, params string[] fieldIds)
    {
        var ids = JsonSerializer.Serialize(fieldIds);
        var desiredChoice = JsonSerializer.Serialize(choiceText?.Trim() ?? "");
        return $$"""
(() => {
  const ids = {{ids}};
  const desiredChoice = {{desiredChoice}};
  const normalized = value => String(value ?? "").trim().toLowerCase();
  function allDocuments(win = window, result = []) {
    try { result.push(win.document); for (const frame of win.frames) allDocuments(frame, result); } catch {}
    return result;
  }
  function visible(element) {
    if (!element) return false;
    const rect = element.getBoundingClientRect();
    const style = element.ownerDocument.defaultView.getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
  }
  for (const doc of allDocuments()) {
    for (const id of ids) {
      const direct = doc.getElementById(id);
      const candidates = [
        direct,
        doc.querySelector(`#s2id_${id} input`),
        doc.querySelector(`#s2id_${id} .select2-choice`),
        doc.querySelector(`[name="${id.replace("sp_formfield_", "")}"]`)
      ].filter(Boolean);
      let target = candidates.find(visible) || direct;
      if (!target) continue;
      try { target.scrollIntoView({ block: "center", inline: "nearest" }); } catch {}
      try { target.focus({ preventScroll: true }); } catch { try { target.focus(); } catch {} }
      if (target.tagName !== "SELECT" && (target !== direct || desiredChoice)) {
        try { target.click(); } catch {}
        const search = doc.querySelector(".select2-drop-active input.select2-input, .select2-drop-active input");
        if (search) { target = search; try { target.focus(); } catch {} }
      }
      let optionIndex = -1;
      if (target.tagName === "SELECT" && desiredChoice) {
        optionIndex = [...target.options].findIndex(option =>
          normalized(option.text) === normalized(desiredChoice) ||
          normalized(option.label) === normalized(desiredChoice) ||
          normalized(option.value) === normalized(desiredChoice));
      }
      return `READY|${target.tagName || "UNKNOWN"}|${target.id || id}|option-index=${optionIndex}`;
    }
  }
  return "WAIT|field=loading";
})()
""";
    }

    public static async Task<string> OpenAndAutofillAsync(AppConfig config, CachedWindowsSnapshot hardware)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("ServiceNow browser autofill is available in the packaged macOS app.");
        var sessionToken = Guid.NewGuid().ToString("N");
        var url = AddOrReplaceQueryParameter(BuildAutofillUrl(config, hardware), "laptopqa_session", sessionToken);
        var browser = FindSupportedBrowser();
        if (!await TryReuseLaptopQaTabAsync(browser, url))
        {
            var open = new ProcessStartInfo { FileName = "/usr/bin/open", UseShellExecute = false, CreateNoWindow = true };
            open.ArgumentList.Add("-a");
            open.ArgumentList.Add(browser);
            open.ArgumentList.Add(url);
            if (Process.Start(open) is null) throw new InvalidOperationException("macOS could not open ServiceNow.");
        }

        await Task.Delay(Math.Clamp(config.ServiceNowAutomationDelayMilliseconds, 500, 30000));
        var typeOfRequest = string.IsNullOrWhiteSpace(config.ServiceNowTypeOfRequest) ? "Other" : config.ServiceNowTypeOfRequest.Trim();
        await FocusAndEnterAsync(browser, sessionToken, BuildFocusScript(typeOfRequest, "sp_formfield_type_of_request"), typeOfRequest, isChoice: true);
        await Task.Delay(450);
        var assignmentGroup = config.ServiceNowAssignmentGroupName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(assignmentGroup))
        {
            await FocusAndEnterAsync(browser, sessionToken, BuildFocusScript(assignmentGroup, "sp_formfield_assignment_group"), assignmentGroup, isChoice: true);
            await Task.Delay(500);
        }
        await FocusAndEnterAsync(browser, sessionToken, BuildFocusScript(null,
            "sp_formfield_describe_request",
            "sp_formfield_please_describe_your_request",
            "sp_formfield_description"), BuildDescription(hardware), isChoice: false);
        await Task.Delay(750);

        var encodedScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildAutofillScript(config, hardware)));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var lastError = "";
        var lastStatus = "";
        while (DateTime.UtcNow < deadline)
        {
            var result = await TryBrowserAsync(browser, encodedScript, sessionToken);
            if (result.Complete) return browser;
            if (!string.IsNullOrWhiteSpace(result.Status)) lastStatus = result.Status;
            if (!string.IsNullOrWhiteSpace(result.Error)) lastError = result.Error;
            if (result.Stopped)
            {
                throw new InvalidOperationException("ServiceNow kept one or more catalog fields invalid after a single committed update. Laptop QA stopped without repeatedly changing the page."
                    + (string.IsNullOrWhiteSpace(result.Status) ? "" : $" Field status: {result.Status}."));
            }
            if (IsPermanentAutomationError(result.Error))
            {
                throw new InvalidOperationException(AutomationPermissionMessage(browser, result.Error));
            }
            await Task.Delay(750);
        }
        throw new InvalidOperationException(BuildIncompleteFieldsMessage(lastStatus, lastError));
    }

    private static async Task FocusAndEnterAsync(string browser, string sessionToken, string focusScript, string text, bool isChoice)
    {
        var encodedFocusScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(focusScript));
        var deadline = DateTime.UtcNow.AddSeconds(35);
        var lastStatus = "";
        while (DateTime.UtcNow < deadline)
        {
            var result = await TryBrowserAsync(browser, encodedFocusScript, sessionToken);
            if (!string.IsNullOrWhiteSpace(result.Status)) lastStatus = result.Status;
            if (IsPermanentAutomationError(result.Error))
                throw new InvalidOperationException(AutomationPermissionMessage(browser, result.Error));
            if (result.Status.StartsWith("READY|", StringComparison.Ordinal))
            {
                var nativeSelect = result.Status.StartsWith("READY|SELECT|", StringComparison.Ordinal);
                var optionIndex = result.Status.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => part.StartsWith("option-index=", StringComparison.OrdinalIgnoreCase))
                    .Select(part => int.TryParse(part[13..], out var parsed) ? parsed : -1)
                    .FirstOrDefault(-1);
                await SendTrustedTextAsync(text, isChoice, nativeSelect, optionIndex);
                return;
            }
            await Task.Delay(500);
        }
        throw new InvalidOperationException("ServiceNow opened, but the requested catalog field did not become available for macOS input."
            + (string.IsNullOrWhiteSpace(lastStatus) ? "" : $" Field status: {lastStatus}."));
    }

    private static async Task SendTrustedTextAsync(string text, bool isChoice, bool nativeSelect, int optionIndex)
    {
        var encodedText = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        string inputSteps;
        if (isChoice && nativeSelect)
        {
            inputSteps = optionIndex >= 0
                ? $"key code 115\ndelay 0.15\nrepeat {optionIndex} times\n  key code 125\n  delay 0.08\nend repeat\ndelay 0.2\nkey code 48"
                : "keystroke entryText\ndelay 0.25\nkey code 48";
        }
        else if (isChoice)
        {
            inputSteps = "set the clipboard to entryText\nkeystroke \"a\" using command down\ndelay 0.1\nkeystroke \"v\" using command down\ndelay 1.5\nkey code 125\ndelay 0.15\nkey code 36\ndelay 0.2\nkey code 48";
        }
        else
        {
            inputSteps = "set the clipboard to entryText\nkeystroke \"a\" using command down\ndelay 0.1\nkeystroke \"v\" using command down\ndelay 0.25\nkey code 48";
        }

        var script = $$"""
set entryText to do shell script "printf %s '{{encodedText}}' | /usr/bin/base64 -D"
tell application "System Events"
  {{inputSteps}}
end tell
return "OK"
""";
        var info = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The macOS keyboard input service could not start.");
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode == 0 && output.Trim().Equals("OK", StringComparison.Ordinal)) return;
        if (error.Contains("assistive access", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("not allowed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("(-1719)", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("macOS is blocking trusted ServiceNow keyboard input. Open the Apple menu > System Settings > Privacy & Security > Accessibility, then turn on Laptop QA. If Laptop QA is not listed, click +, select the Laptop QA app you launch, click Open, and turn it on. Then press ServiceNow again.");
        }
        throw new InvalidOperationException("macOS could not enter the ServiceNow field using trusted keyboard input."
            + (string.IsNullOrWhiteSpace(error) ? "" : $" Response: {error.Trim()}"));
    }

    private static string FindSupportedBrowser()
    {
        foreach (var candidate in new[]
                 {
                     (Name: "Microsoft Edge", Path: "/Applications/Microsoft Edge.app"),
                     (Name: "Google Chrome", Path: "/Applications/Google Chrome.app"),
                     (Name: "Safari", Path: "/Applications/Safari.app")
                 })
        {
            if (Directory.Exists(candidate.Path)) return candidate.Name;
        }
        throw new InvalidOperationException("Laptop QA needs Microsoft Edge, Google Chrome, or Safari to fill the ServiceNow form on macOS.");
    }

    private static async Task<bool> TryReuseLaptopQaTabAsync(string browser, string targetUrl)
    {
        var encodedUrl = Convert.ToBase64String(Encoding.UTF8.GetBytes(targetUrl));
        var catalogItemId = new Uri(targetUrl).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && Uri.UnescapeDataString(parts[0]).Equals("sys_id", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault() ?? "";
        var encodedCatalogItemId = Convert.ToBase64String(Encoding.UTF8.GetBytes(catalogItemId));
        var activateTab = browser == "Safari"
            ? "set current tab of browserWindow to browserTab"
            : "set active tab index of browserWindow to tabIndex";
        var script = $$"""
set targetUrl to do shell script "printf %s '{{encodedUrl}}' | /usr/bin/base64 -D"
set catalogItemId to do shell script "printf %s '{{encodedCatalogItemId}}' | /usr/bin/base64 -D"
set foundLaptopQaTab to false
if application "{{browser}}" is running then
  tell application "{{browser}}"
    repeat with browserWindow in windows
      set tabCount to count of tabs of browserWindow
      repeat with tabIndex from tabCount to 1 by -1
        try
          set browserTab to tab tabIndex of browserWindow
          set pageUrl to URL of browserTab
          if pageUrl contains "laptopqa_session=" or (catalogItemId is not "" and pageUrl contains ("sys_id=" & catalogItemId)) then
            if foundLaptopQaTab is false then
              {{activateTab}}
              set URL of browserTab to "about:blank"
              delay 0.25
              set URL of browserTab to targetUrl
              set foundLaptopQaTab to true
            else
              try
                close browserTab
              end try
            end if
          end if
        end try
      end repeat
    end repeat
    if foundLaptopQaTab then activate
  end tell
end if
if foundLaptopQaTab then return "REUSED"
return "NONE"
""";
        var info = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-");
        using var process = Process.Start(info);
        if (process is null) return false;
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        _ = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && output.Trim().Equals("REUSED", StringComparison.Ordinal);
    }

    private static string BuildIncompleteFieldsMessage(string status, string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return "ServiceNow opened, but the browser returned an automation error."
                   + $" Response: {error.Trim()}";

        var missing = new List<string>();
        if (status.Contains("type=false", StringComparison.OrdinalIgnoreCase)) missing.Add("Type of request");
        if (status.Contains("group=false", StringComparison.OrdinalIgnoreCase)) missing.Add("Assignment group");
        if (status.Contains("description=false", StringComparison.OrdinalIgnoreCase)) missing.Add("Description");
        if (missing.Count == 0)
            return "ServiceNow opened, but the request form did not finish loading in time. The app stopped without repeatedly changing the page.";

        return $"ServiceNow opened, but Laptop QA could not confirm: {string.Join(", ", missing)}. " +
               "The app stopped without repeatedly changing the page. Please review those fields before submitting.";
    }

    private static async Task<(bool Complete, bool Stopped, string Status, string Error)> TryBrowserAsync(string browser, string encodedScript, string sessionToken)
    {
        var execute = browser == "Safari"
            ? "repeat with browserWindow in windows\nrepeat with browserTab in tabs of browserWindow\nset pageUrl to URL of browserTab\nif pageUrl contains \"service-now.com\" then\nset current tab of browserWindow to browserTab\nactivate\nset jsResult to do JavaScript jsCode in browserTab\nreturn \"RESULT|\" & jsResult\nend if\nend repeat\nend repeat"
            : "repeat with browserWindow in windows\nset tabCount to count of tabs of browserWindow\nrepeat with tabIndex from 1 to tabCount\nset browserTab to tab tabIndex of browserWindow\nset pageUrl to URL of browserTab\nif pageUrl contains \"service-now.com\" then\nset active tab index of browserWindow to tabIndex\nactivate\nset jsResult to execute browserTab javascript jsCode\nreturn \"RESULT|\" & jsResult\nend if\nend repeat\nend repeat";
        execute = execute.Replace("service-now.com", $"laptopqa_session={sessionToken}", StringComparison.Ordinal);
        var script = $$"""
set jsCode to do shell script "printf %s '{{encodedScript}}' | /usr/bin/base64 -D"
if application "{{browser}}" is running then
  tell application "{{browser}}"
    if (count of windows) > 0 then
      {{execute}}
    end if
  end tell
end if
return "WAIT"
""";
        var info = new ProcessStartInfo { FileName = "/usr/bin/osascript", UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The macOS browser automation service could not start.");
        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var status = output.Trim();
        return (process.ExitCode == 0 && status.StartsWith("RESULT|COMPLETE|", StringComparison.Ordinal),
            process.ExitCode == 0 && status.StartsWith("RESULT|STOP|", StringComparison.Ordinal),
            status.StartsWith("RESULT|", StringComparison.Ordinal) ? status[7..] : status,
            process.ExitCode == 0 ? "" : error.Trim());
    }

    private static bool IsPermanentAutomationError(string error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        return error.Contains("not authorized to send apple events", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("javascript from apple events", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("executing javascript through applescript is turned off", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("(-1743)", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("(-1002)", StringComparison.OrdinalIgnoreCase);
    }

    private static string AutomationPermissionMessage(string browser, string error)
    {
        if (error.Contains("javascript", StringComparison.OrdinalIgnoreCase) || error.Contains("-1002", StringComparison.OrdinalIgnoreCase))
        {
            return $"{browser} is blocking ServiceNow field filling. In {browser}, enable View > Developer > Allow JavaScript from Apple Events, then press ServiceNow again.";
        }

        return $"macOS is blocking Laptop QA from controlling {browser}. Open System Settings > Privacy & Security > Automation, allow Laptop QA to control {browser}, then press ServiceNow again.";
    }

    private static string ValidateUrl(string? candidate)
    {
        var requestUrl = candidate?.Trim() ?? "";
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var requestUri) || (requestUri.Scheme != Uri.UriSchemeHttps && requestUri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("The ServiceNow request URL in Config is not a valid web address.");
        return requestUri.AbsoluteUri;
    }

    private static string AddOrReplaceQueryParameter(string url, string name, string value)
    {
        var uri = new Uri(url, UriKind.Absolute);
        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.Split('=', 2)[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        queryParts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        var builder = new UriBuilder(uri) { Query = string.Join("&", queryParts) };
        return builder.Uri.AbsoluteUri;
    }
}
