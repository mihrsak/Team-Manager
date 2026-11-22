(() => {
    const message = document.body.dataset.unsavedMessage;
    const initial = new Map();
    let submitting = false;
    function state(form) {
        return JSON.stringify([...form.elements].filter(field => field.name && !['hidden','submit','button'].includes(field.type)).map(field =>
            [field.name, field.type === 'checkbox' || field.type === 'radio' ? field.checked : field.type === 'file'
                ? [...field.files].map(file => [file.name,file.size,file.lastModified]) : field.multiple ? [...field.selectedOptions].map(option => option.value) : field.value]));
    }
    for (const form of document.forms) if (form.method.toLowerCase() === 'post') initial.set(form, state(form));
    const dirty = except => [...initial].some(([form, original]) => form !== except && state(form) !== original);
    window.addEventListener('beforeunload', event => {
        if (!submitting && dirty()) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('click', event => {
        const link = event.target.closest('a[href]');
        if (!link || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.target === '_blank' || link.hasAttribute('download')) return;
        const url = new URL(link.href, location.href);
        if (url.origin === location.origin && url.pathname === location.pathname && url.search === location.search && url.hash) return;
        if (dirty() && !window.confirm(message)) event.preventDefault();
        else submitting = true;
    });
    document.addEventListener('submit', event => {
        if (dirty(event.target) && !window.confirm(message)) { event.preventDefault(); return; }
        submitting = true;
    });
    const errors = document.querySelector('[data-validation-focus]');
    if (errors) errors.focus();
    for (const notice of document.querySelectorAll('.notice')) notice.setAttribute('role','status');
    for (const kind of document.querySelectorAll('[data-lookup-kind]')) {
        const name=kind.form.elements.namedItem('name');
        const update=()=>{name.maxLength={type:50,status:30,priority:20}[kind.value]||50;};
        kind.addEventListener('change',update); update();
    }
})();
