// Modern Client Interaction for Universal AI Job Finder
document.addEventListener('DOMContentLoaded', () => {
    initTheme();
    initOllamaStatus();
    initSettingsModal();
    initDropzone();
    initUrlInputs();
    initDemoAndClear();
    initFormSubmission();
    initResumeProfileModal();
});

// --- Theme Toggling ---
function initTheme() {
    const themeBtn = document.getElementById('themeToggleBtn');
    const themeIcon = document.getElementById('themeIcon');
    const html = document.documentElement;

    const savedTheme = localStorage.getItem('ai_job_theme') || 'dark';
    html.setAttribute('data-bs-theme', savedTheme);
    updateThemeIcon(savedTheme);

    themeBtn?.addEventListener('click', () => {
        const currentTheme = html.getAttribute('data-bs-theme');
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        html.setAttribute('data-bs-theme', newTheme);
        localStorage.setItem('ai_job_theme', newTheme);
        updateThemeIcon(newTheme);
    });

    function updateThemeIcon(theme) {
        if (!themeIcon) return;
        if (theme === 'dark') {
            themeIcon.className = 'bi bi-moon-stars-fill';
        } else {
            themeIcon.className = 'bi bi-sun-fill';
        }
    }
}

// --- Ollama Health Check & Real-Time Status ---
function initOllamaStatus() {
    const dot = document.getElementById('ollamaStatusDot');
    const text = document.getElementById('ollamaStatusText');

    async function checkStatus() {
        const customBaseUrl = localStorage.getItem('ai_job_ollama_url') || 'http://localhost:11434';
        try {
            const resp = await fetch(`/Home/CheckOllamaStatus?baseUrl=${encodeURIComponent(customBaseUrl)}`);
            const data = await resp.json();

            if (data.isConnected) {
                if (dot) dot.className = 'status-dot status-dot-connected';
                if (text) text.textContent = 'Ollama Connected';
            } else {
                if (dot) dot.className = 'status-dot status-dot-disconnected';
                if (text) text.textContent = 'Ollama Offline';
            }
        } catch {
            if (dot) dot.className = 'status-dot status-dot-disconnected';
            if (text) text.textContent = 'Ollama Offline';
        }
    }

    checkStatus();
    setInterval(checkStatus, 30000);
}

// --- Settings Modal & LocalStorage Persistence ---
function initSettingsModal() {
    const inputUrl = document.getElementById('settingsBaseUrl');
    const inputChat = document.getElementById('settingsChatModel');
    const inputEmbed = document.getElementById('settingsEmbeddingModel');
    const btnSave = document.getElementById('btnSaveSettings');
    const btnTest = document.getElementById('btnTestOllama');
    const testFeedback = document.getElementById('ollamaTestFeedback');

    const hiddenBaseUrl = document.getElementById('hiddenBaseUrl');
    const hiddenModel = document.getElementById('hiddenModel');
    const hiddenEmbeddingModel = document.getElementById('hiddenEmbeddingModel');

    const savedUrl = localStorage.getItem('ai_job_ollama_url') || 'http://localhost:11434';
    const savedChat = localStorage.getItem('ai_job_ollama_chat') || 'llama3.1:8b';
    const savedEmbed = localStorage.getItem('ai_job_ollama_embed') || 'nomic-embed-text';

    if (inputUrl) inputUrl.value = savedUrl;
    if (inputChat) inputChat.value = savedChat;
    if (inputEmbed) inputEmbed.value = savedEmbed;

    if (hiddenBaseUrl) hiddenBaseUrl.value = savedUrl;
    if (hiddenModel) hiddenModel.value = savedChat;
    if (hiddenEmbeddingModel) hiddenEmbeddingModel.value = savedEmbed;

    btnSave?.addEventListener('click', () => {
        const url = inputUrl?.value.trim() || 'http://localhost:11434';
        const chat = inputChat?.value.trim() || 'llama3.1:8b';
        const embed = inputEmbed?.value.trim() || 'nomic-embed-text';

        localStorage.setItem('ai_job_ollama_url', url);
        localStorage.setItem('ai_job_ollama_chat', chat);
        localStorage.setItem('ai_job_ollama_embed', embed);

        if (hiddenBaseUrl) hiddenBaseUrl.value = url;
        if (hiddenModel) hiddenModel.value = chat;
        if (hiddenEmbeddingModel) hiddenEmbeddingModel.value = embed;
    });

    btnTest?.addEventListener('click', async () => {
        const url = inputUrl?.value.trim() || 'http://localhost:11434';
        if (testFeedback) {
            testFeedback.textContent = 'Testing connection...';
            testFeedback.className = 'form-text text-warning';
        }

        try {
            const resp = await fetch(`/Home/CheckOllamaStatus?baseUrl=${encodeURIComponent(url)}`);
            const data = await resp.json();

            if (data.isConnected) {
                if (testFeedback) {
                    testFeedback.textContent = `Connected! ${data.availableModels.length} models found.`;
                    testFeedback.className = 'form-text text-success';
                }
            } else {
                if (testFeedback) {
                    testFeedback.textContent = 'Connection failed. Ensure Ollama is running.';
                    testFeedback.className = 'form-text text-danger';
                }
            }
        } catch (err) {
            if (testFeedback) {
                testFeedback.textContent = `Error: ${err.message}`;
                testFeedback.className = 'form-text text-danger';
            }
        }
    });
}

// --- Resume Drag-and-Drop & Interactive Preview ---
function initDropzone() {
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('resumeFileInput');
    const btnBrowse = document.getElementById('btnBrowseFile');
    const previewCard = document.getElementById('filePreviewCard');
    const previewName = document.getElementById('filePreviewName');
    const previewSize = document.getElementById('filePreviewSize');
    const previewSnippet = document.getElementById('previewProfileSnippet');
    const btnRemove = document.getElementById('btnRemoveFile');

    if (!dropzone || !fileInput) return;

    // Prevent default drag & drop behaviors across the window
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        window.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
        }, false);
        document.body.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
        }, false);
    });

    btnBrowse?.addEventListener('click', (e) => {
        e.stopPropagation();
        fileInput.click();
    });

    dropzone.addEventListener('click', () => fileInput.click());

    // Highlight dropzone on drag
    ['dragenter', 'dragover'].forEach(eventName => {
        dropzone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dragover');
        }, false);
    });

    ['dragleave', 'dragend'].forEach(eventName => {
        dropzone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
        }, false);
    });

    dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        dropzone.classList.remove('dragover');

        const dt = e.dataTransfer;
        if (dt && dt.files && dt.files.length > 0) {
            const droppedFile = dt.files[0];
            try {
                const dataTransfer = new DataTransfer();
                dataTransfer.items.add(droppedFile);
                fileInput.files = dataTransfer.files;
            } catch (err) {
                console.warn('DataTransfer assignment fallback:', err);
            }
            handleFile(droppedFile);
        }
    }, false);

    fileInput.addEventListener('change', () => {
        if (fileInput.files && fileInput.files.length > 0) {
            handleFile(fileInput.files[0]);
        }
    });

    btnRemove?.addEventListener('click', (e) => {
        e.stopPropagation();
        fileInput.value = '';
        if (previewCard) previewCard.classList.add('d-none');
        if (dropzone) dropzone.classList.remove('d-none');
        clearOverriddenFields();
    });

    async function handleFile(file) {
        const maxBytes = 5 * 1024 * 1024;
        const validExtensions = ['.pdf', '.docx', '.txt'];
        const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();

        if (!validExtensions.includes(ext)) {
            alert(`Unsupported file format '${ext}'. Please upload a PDF, DOCX, or TXT file.`);
            fileInput.value = '';
            return;
        }

        if (file.size > maxBytes) {
            alert(`File size (${(file.size / (1024 * 1024)).toFixed(1)} MB) exceeds maximum limit of 5 MB.`);
            fileInput.value = '';
            return;
        }

        if (previewName) previewName.textContent = file.name;
        if (previewSize) previewSize.textContent = formatBytes(file.size);
        if (previewSnippet) previewSnippet.textContent = 'Extracting candidate details...';
        if (previewCard) previewCard.classList.remove('d-none');
        if (dropzone) dropzone.classList.add('d-none');

        const hiddenDemo = document.getElementById('hiddenDemoMode');
        if (hiddenDemo) hiddenDemo.value = 'false';

        // Trigger AJAX Resume Preview Extraction
        try {
            const formData = new FormData();
            formData.append('resumeFile', file);
            
            const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
            if (tokenEl) formData.append('__RequestVerificationToken', tokenEl.value);

            const customBaseUrl = localStorage.getItem('ai_job_ollama_url') || 'http://localhost:11434';
            const customModel = localStorage.getItem('ai_job_ollama_chat') || 'llama3.1:8b';
            formData.append('customBaseUrl', customBaseUrl);
            formData.append('customModel', customModel);

            const resp = await fetch('/Home/ParseResumePreview', {
                method: 'POST',
                body: formData
            });

            const data = await resp.json();
            if (data.success) {
                // Populate Modal Inputs
                setModalField('modalCandidateName', data.candidateName || '');
                setModalField('modalCurrentTitle', data.currentTitle || '');
                setModalField('modalYearsExperience', data.totalYearsExperience || 0);
                setModalField('modalDegree', data.degree || '');
                setModalField('modalSkills', data.skillsString || (data.skills ? data.skills.join(', ') : ''));

                // Save to hidden fields as defaults
                saveModalFieldsToHidden();

                if (previewSnippet) {
                    previewSnippet.textContent = `${data.candidateName || 'Candidate'} (${data.totalYearsExperience || 0} yrs exp, ${data.skills ? data.skills.length : 0} skills)`;
                }

                // Show the Review & Edit Popup
                const modalEl = document.getElementById('resumeProfileModal');
                if (modalEl) {
                    // @ts-ignore
                    const bsModal = new bootstrap.Modal(modalEl);
                    bsModal.show();
                }
            } else {
                if (previewSnippet) previewSnippet.textContent = 'Profile parsed into RAM.';
            }
        } catch (err) {
            console.warn('Resume preview extraction error:', err);
            if (previewSnippet) previewSnippet.textContent = 'Profile ready for comparison.';
        }
    }

    function formatBytes(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    }
}

// --- Resume Profile Review & Edit Modal Handler ---
function initResumeProfileModal() {
    const btnSave = document.getElementById('btnSaveProfileModal');
    const btnEdit = document.getElementById('btnEditExtractedProfile');
    const modalEl = document.getElementById('resumeProfileModal');

    btnSave?.addEventListener('click', () => {
        saveModalFieldsToHidden();
        
        // Update snippet in preview card
        const name = document.getElementById('modalCandidateName')?.value || 'Candidate';
        const yrs = document.getElementById('modalYearsExperience')?.value || '0';
        const skillsText = document.getElementById('modalSkills')?.value || '';
        const skillCount = skillsText.split(',').filter(s => s.trim().length > 0).length;

        const snippet = document.getElementById('previewProfileSnippet');
        if (snippet) snippet.textContent = `${name} (${yrs} yrs exp, ${skillCount} skills)`;

        if (modalEl) {
            // @ts-ignore
            const bsModal = bootstrap.Modal.getInstance(modalEl);
            bsModal?.hide();
        }
    });

    btnEdit?.addEventListener('click', () => {
        if (modalEl) {
            // @ts-ignore
            const bsModal = new bootstrap.Modal(modalEl);
            bsModal.show();
        }
    });
}

function setModalField(id, val) {
    const el = document.getElementById(id);
    if (el) el.value = val;
}

function saveModalFieldsToHidden() {
    setHiddenField('hiddenCandidateName', document.getElementById('modalCandidateName')?.value);
    setHiddenField('hiddenCandidateTitle', document.getElementById('modalCurrentTitle')?.value);
    setHiddenField('hiddenYearsExperience', document.getElementById('modalYearsExperience')?.value);
    setHiddenField('hiddenDegree', document.getElementById('modalDegree')?.value);
    setHiddenField('hiddenSkills', document.getElementById('modalSkills')?.value);
}

function setHiddenField(id, val) {
    const el = document.getElementById(id);
    if (el && val !== undefined) el.value = val;
}

function clearOverriddenFields() {
    setHiddenField('hiddenCandidateName', '');
    setHiddenField('hiddenCandidateTitle', '');
    setHiddenField('hiddenYearsExperience', '');
    setHiddenField('hiddenDegree', '');
    setHiddenField('hiddenSkills', '');
    const hiddenDemo = document.getElementById('hiddenDemoMode');
    if (hiddenDemo) hiddenDemo.value = 'false';
}

// --- Universal URL Inputs & Dynamic Domain Badges ---
function initUrlInputs() {
    const container = document.getElementById('urlInputContainer');
    const btnPaste = document.getElementById('btnPasteClipboard');

    if (!container) return;

    // Attach input listener to all URL fields
    container.querySelectorAll('.url-field').forEach(input => {
        input.addEventListener('input', () => updateIdBadge(input));
        updateIdBadge(input);
    });

    // Clear buttons
    container.querySelectorAll('.btn-clear-url').forEach(btn => {
        btn.addEventListener('click', () => {
            const targetIdx = btn.getAttribute('data-target');
            const input = document.getElementById(`jobUrlInput_${targetIdx}`);
            if (input) {
                input.value = '';
                updateIdBadge(input);
            }
        });
    });

    // Paste from clipboard
    btnPaste?.addEventListener('click', async () => {
        try {
            const text = await navigator.clipboard.readText();
            if (!text) return;

            const emptyInput = Array.from(container.querySelectorAll('.url-field')).find(i => !i.value.trim());
            if (emptyInput) {
                emptyInput.value = text.trim();
                updateIdBadge(emptyInput);
            } else {
                alert('All 5 URL slots are filled. Clear one to paste a new URL.');
            }
        } catch (err) {
            console.warn('Clipboard read failed:', err);
        }
    });

    function updateIdBadge(input) {
        const row = input.closest('.url-input-row');
        if (!row) return;
        const rowIdx = row.getAttribute('data-row');
        const badgeSlot = document.getElementById(`idBadge_${rowIdx}`);
        const idText = badgeSlot?.querySelector('.id-text');

        const val = input.value.trim();
        if (!val) {
            if (badgeSlot) badgeSlot.classList.add('d-none');
            return;
        }

        const domain = detectDomain(val);
        if (badgeSlot && idText) {
            idText.textContent = domain;
            badgeSlot.classList.remove('d-none');
        }
    }

    function detectDomain(url) {
        if (!url) return 'Web';
        const trimmed = url.trim().toLowerCase();
        if (/^\d{4,10}$/.test(trimmed)) return 'Bdjobs';
        if (trimmed.includes('bdjobs.com')) return 'Bdjobs';
        if (trimmed.includes('linkedin.com')) return 'LinkedIn';
        if (trimmed.includes('greenhouse.io')) return 'Greenhouse';
        if (trimmed.includes('lever.co')) return 'Lever';
        if (trimmed.includes('indeed.com')) return 'Indeed';
        if (trimmed.includes('ashbyhq.com')) return 'Ashby';
        if (trimmed.includes('workable.com')) return 'Workable';
        if (trimmed.includes('glassdoor.com')) return 'Glassdoor';
        if (trimmed.includes('wellfound.com') || trimmed.includes('angel.co')) return 'Wellfound';

        try {
            const uri = new URL(trimmed.startsWith('http') ? trimmed : `https://${trimmed}`);
            const host = uri.hostname.replace('www.', '').replace('jobs.', '').replace('careers.', '');
            const part = host.split('.')[0];
            return part ? part.charAt(0).toUpperCase() + part.slice(1) : 'Web';
        } catch {
            return 'Web';
        }
    }
}

// --- Demo Scenario & Clear Form ---
function initDemoAndClear() {
    const btnDemo = document.getElementById('btnLoadDemo');
    const btnClear = document.getElementById('btnClearForm');

    btnDemo?.addEventListener('click', async () => {
        try {
            const resp = await fetch('/Home/GetSamplePresets');
            const data = await resp.json();

            // Populate URLs
            if (data.urls && data.urls.length > 0) {
                data.urls.forEach((url, idx) => {
                    const input = document.getElementById(`jobUrlInput_${idx}`);
                    if (input) {
                        input.value = url;
                        input.dispatchEvent(new Event('input'));
                    }
                });
            }

            // Set preview card to sample candidate
            const previewCard = document.getElementById('filePreviewCard');
            const previewName = document.getElementById('filePreviewName');
            const previewSize = document.getElementById('filePreviewSize');
            const previewSnippet = document.getElementById('previewProfileSnippet');
            const dropzone = document.getElementById('dropzone');
            const hiddenDemo = document.getElementById('hiddenDemoMode');

            if (previewName) previewName.textContent = data.candidateName || 'Rahim Ahmed (Senior .NET & AI Engineer)';
            if (previewSize) previewSize.textContent = 'Demo Preloaded Profile';
            if (previewSnippet) previewSnippet.textContent = 'Rahim Ahmed (5 yrs exp, 12 skills)';
            if (previewCard) previewCard.classList.remove('d-none');
            if (dropzone) dropzone.classList.add('d-none');
            if (hiddenDemo) hiddenDemo.value = 'true';

            // Populate modal inputs with sample candidate
            setModalField('modalCandidateName', 'Rahim Ahmed');
            setModalField('modalCurrentTitle', 'Senior Full Stack Software Engineer');
            setModalField('modalYearsExperience', 5);
            setModalField('modalDegree', 'B.Sc. in Computer Science & Engineering (BUET)');
            setModalField('modalSkills', 'C#, ASP.NET Core, SQL Server, PostgreSQL, Redis, Docker, Kubernetes, Angular, React, Ollama, RAG, Python');
            saveModalFieldsToHidden();

        } catch (err) {
            console.error('Failed to load demo presets:', err);
        }
    });

    btnClear?.addEventListener('click', () => {
        document.querySelectorAll('.url-field').forEach(i => {
            i.value = '';
            i.dispatchEvent(new Event('input'));
        });
        document.querySelectorAll('textarea[name^="ManualJdTexts"]').forEach(t => t.value = '');
        const btnRemove = document.getElementById('btnRemoveFile');
        btnRemove?.click();
    });
}

// --- Form Submission & Animated Progress Modal ---
function initFormSubmission() {
    const form = document.getElementById('jobFinderForm');
    const progressModalEl = document.getElementById('progressModal');
    if (!form || !progressModalEl) return;

    let progressModal = null;

    form.addEventListener('submit', (e) => {
        const fileInput = document.getElementById('resumeFileInput');
        const hiddenDemo = document.getElementById('hiddenDemoMode');
        const hasFile = fileInput && fileInput.files && fileInput.files.length > 0;
        const isDemo = hiddenDemo && hiddenDemo.value === 'true';

        if (!hasFile && !isDemo) {
            e.preventDefault();
            alert('Please upload a resume file (PDF or DOCX) or click "Try Demo Preset".');
            return;
        }

        const urls = Array.from(document.querySelectorAll('.url-field'))
            .map(i => i.value.trim())
            .filter(u => u.length > 0);

        const manualTexts = Array.from(document.querySelectorAll('textarea[name^="ManualJdTexts"]'))
            .map(t => t.value.trim())
            .filter(t => t.length > 0);

        if (urls.length === 0 && manualTexts.length === 0 && !isDemo) {
            e.preventDefault();
            alert('Please enter at least one job URL or paste a job description.');
            return;
        }

        // Show animated progress modal
        // @ts-ignore
        progressModal = new bootstrap.Modal(progressModalEl);
        progressModal.show();
        startProgressSimulation();
    });

    function startProgressSimulation() {
        const steps = [
            { id: 'step1', pct: 20, text: 'Parsing resume text & chunking qualifications...' },
            { id: 'step2', pct: 40, text: 'Extracting structured data from job postings...' },
            { id: 'step3', pct: 60, text: 'Generating in-memory vector embeddings...' },
            { id: 'step4', pct: 80, text: 'RAG structured profile & skill extraction...' },
            { id: 'step5', pct: 95, text: 'Deterministic 5D scoring & rationale generation...' }
        ];

        const bar = document.getElementById('progressBar');
        const title = document.getElementById('progressStatusTitle');
        const timer = document.getElementById('elapsedTimer');

        let seconds = 0;
        const timerInterval = setInterval(() => {
            seconds++;
            if (timer) timer.textContent = `${seconds}s`;
        }, 1000);

        let currentStep = 0;
        const stepInterval = setInterval(() => {
            if (currentStep < steps.length) {
                const s = steps[currentStep];
                if (bar) bar.style.width = `${s.pct}%`;
                if (title) title.textContent = s.text;

                steps.forEach((step, idx) => {
                    const el = document.getElementById(step.id);
                    if (!el) return;
                    if (idx < currentStep) {
                        el.className = 'd-flex align-items-center gap-2 extra-small progress-step step-completed';
                        const icon = el.querySelector('i');
                        if (icon) icon.className = 'bi bi-check-circle-fill text-success';
                    } else if (idx === currentStep) {
                        el.className = 'd-flex align-items-center gap-2 extra-small progress-step step-active';
                        const icon = el.querySelector('i');
                        if (icon) icon.className = 'bi bi-arrow-repeat text-primary spin';
                    }
                });

                currentStep++;
            } else {
                clearInterval(stepInterval);
            }
        }, 2200);
    }
}
