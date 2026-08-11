// AI Job Finder Client Interaction Script
document.addEventListener('DOMContentLoaded', () => {
    initThemeToggle();
    initOllamaStatus();
    initDropzone();
    initUrlInputs();
    initDemoAndClear();
    initFormSubmission();
});

// --- Theme Switcher ---
function initThemeToggle() {
    const themeToggleBtn = document.getElementById('themeToggleBtn');
    const themeIcon = document.getElementById('themeIcon');
    const htmlEl = document.documentElement;

    const savedTheme = localStorage.getItem('theme') || 'dark';
    htmlEl.setAttribute('data-bs-theme', savedTheme);
    updateThemeIcon(savedTheme);

    themeToggleBtn?.addEventListener('click', () => {
        const currentTheme = htmlEl.getAttribute('data-bs-theme');
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
        htmlEl.setAttribute('data-bs-theme', newTheme);
        localStorage.setItem('theme', newTheme);
        updateThemeIcon(newTheme);
    });

    function updateThemeIcon(theme) {
        if (!themeIcon) return;
        if (theme === 'dark') {
            themeIcon.className = 'bi bi-moon-stars-fill text-warning';
        } else {
            themeIcon.className = 'bi bi-sun-fill text-warning';
        }
    }
}

// --- Ollama Health Check & Settings ---
function initOllamaStatus() {
    const statusDot = document.getElementById('ollamaStatusDot');
    const statusText = document.getElementById('ollamaStatusText');
    const btnTest = document.getElementById('btnTestOllama');
    const btnSave = document.getElementById('btnSaveSettings');
    const feedback = document.getElementById('ollamaTestFeedback');

    const inputBaseUrl = document.getElementById('settingsBaseUrl');
    const inputChatModel = document.getElementById('settingsChatModel');
    const inputEmbeddingModel = document.getElementById('settingsEmbeddingModel');

    // Load stored settings or defaults
    const storedUrl = localStorage.getItem('ollama_url') || 'http://localhost:11434';
    const storedChatModel = localStorage.getItem('ollama_chat_model') || 'llama3.1:8b';
    const storedEmbeddingModel = localStorage.getItem('ollama_embedding_model') || 'nomic-embed-text';

    if (inputBaseUrl) inputBaseUrl.value = storedUrl;
    if (inputChatModel) inputChatModel.value = storedChatModel;
    if (inputEmbeddingModel) inputEmbeddingModel.value = storedEmbeddingModel;

    syncHiddenInputs();

    // Check status
    checkStatus(storedUrl);

    btnTest?.addEventListener('click', () => {
        const url = inputBaseUrl?.value || 'http://localhost:11434';
        if (feedback) feedback.textContent = 'Testing connection...';
        checkStatus(url, true);
    });

    btnSave?.addEventListener('click', () => {
        if (inputBaseUrl) localStorage.setItem('ollama_url', inputBaseUrl.value.trim());
        if (inputChatModel) localStorage.setItem('ollama_chat_model', inputChatModel.value.trim());
        if (inputEmbeddingModel) localStorage.setItem('ollama_embedding_model', inputEmbeddingModel.value.trim());
        syncHiddenInputs();
        checkStatus(inputBaseUrl?.value || storedUrl);
    });

    function syncHiddenInputs() {
        const hBase = document.getElementById('hiddenBaseUrl');
        const hModel = document.getElementById('hiddenModel');
        const hEmb = document.getElementById('hiddenEmbeddingModel');

        if (hBase) hBase.value = localStorage.getItem('ollama_url') || '';
        if (hModel) hModel.value = localStorage.getItem('ollama_chat_model') || '';
        if (hEmb) hEmb.value = localStorage.getItem('ollama_embedding_model') || '';
    }

    async function checkStatus(url, isManualTest = false) {
        try {
            if (statusDot) statusDot.className = 'status-dot status-dot-checking';
            if (statusText) statusText.textContent = 'Checking...';

            const resp = await fetch(`/Home/CheckOllamaStatus?baseUrl=${encodeURIComponent(url)}`);
            if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const data = await resp.json();

            if (data.isConnected) {
                if (statusDot) statusDot.className = 'status-dot status-dot-connected';
                const modelCount = data.availableModels ? data.availableModels.length : 0;
                if (statusText) statusText.textContent = `Ollama Online (${modelCount} models)`;
                if (feedback && isManualTest) {
                    feedback.innerHTML = `<span class="text-success"><i class="bi bi-check-circle-fill"></i> Connected! Found models: ${data.availableModels.join(', ')}</span>`;
                }
            } else {
                if (statusDot) statusDot.className = 'status-dot status-dot-disconnected';
                if (statusText) statusText.textContent = 'Ollama Offline';
                if (feedback && isManualTest) {
                    feedback.innerHTML = `<span class="text-danger"><i class="bi bi-x-circle-fill"></i> ${data.errorMessage || 'Cannot connect to Ollama'}</span>`;
                }
            }
        } catch (err) {
            if (statusDot) statusDot.className = 'status-dot status-dot-disconnected';
            if (statusText) statusText.textContent = 'Ollama Offline';
            if (feedback && isManualTest) {
                feedback.innerHTML = `<span class="text-danger"><i class="bi bi-x-circle-fill"></i> Connection failed (${err.message})</span>`;
            }
        }
    }
}

// --- Resume Dropzone & File Handling ---
function initDropzone() {
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('resumeFileInput');
    const btnBrowse = document.getElementById('btnBrowseFile');
    const previewCard = document.getElementById('filePreviewCard');
    const previewName = document.getElementById('filePreviewName');
    const previewSize = document.getElementById('filePreviewSize');
    const btnRemove = document.getElementById('btnRemoveFile');

    if (!dropzone || !fileInput) return;

    btnBrowse?.addEventListener('click', (e) => {
        e.stopPropagation();
        fileInput.click();
    });

    dropzone.addEventListener('click', () => fileInput.click());

    dropzone.addEventListener('dragover', (e) => {
        e.preventDefault();
        dropzone.classList.add('dragover');
    });

    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('dragover'));

    dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('dragover');
        if (e.dataTransfer.files.length > 0) {
            handleFile(e.dataTransfer.files[0]);
        }
    });

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
        const hiddenDemo = document.getElementById('hiddenDemoMode');
        if (hiddenDemo) hiddenDemo.value = 'false';
    });

    function handleFile(file) {
        const maxBytes = 5 * 1024 * 1024;
        const validExtensions = ['.pdf', '.docx', '.txt'];
        const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();

        if (!validExtensions.includes(ext)) {
            alert(`Unsupported file format '${ext}'. Please upload a PDF, DOCX, or TXT file.`);
            return;
        }

        if (file.size > maxBytes) {
            alert(`File size (${(file.size / (1024 * 1024)).toFixed(1)} MB) exceeds maximum limit of 5 MB.`);
            return;
        }

        if (previewName) previewName.textContent = file.name;
        if (previewSize) previewSize.textContent = formatBytes(file.size);
        if (previewCard) previewCard.classList.remove('d-none');
        if (dropzone) dropzone.classList.add('d-none');

        const hiddenDemo = document.getElementById('hiddenDemoMode');
        if (hiddenDemo) hiddenDemo.value = 'false';
    }

    function formatBytes(bytes) {
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    }
}

// --- Bdjobs URL Inputs & Dynamic ID Detection ---
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

            // Find first empty field
            const emptyInput = Array.from(container.querySelectorAll('.url-field')).find(i => !i.value.trim());
            if (emptyInput) {
                emptyInput.value = text.trim();
                updateIdBadge(emptyInput);
            } else {
                alert('All 5 URL slots are already filled. Clear one to paste a new URL.');
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
        const jobId = extractJobId(val);

        if (jobId && badgeSlot && idText) {
            idText.textContent = jobId;
            badgeSlot.classList.remove('d-none');
        } else if (badgeSlot) {
            badgeSlot.classList.add('d-none');
        }
    }

    function extractJobId(url) {
        if (!url) return null;
        const trimmed = url.trim();
        if (/^\d{4,10}$/.test(trimmed)) return trimmed;
        const queryMatch = trimmed.match(/(?:[?&](?:job)?id=)(\d+)/i);
        if (queryMatch) return queryMatch[1];
        const pathMatch = trimmed.match(/(?:\/(?:[a-zA-Z0-9_-]+\/)?(?:details|jobs|jobdetails)\/)(\d+)/i);
        if (pathMatch) return pathMatch[1];
        const anyMatch = trimmed.match(/\b(\d{5,10})\b/);
        return anyMatch ? anyMatch[1] : null;
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
            const dropzone = document.getElementById('dropzone');
            const hiddenDemo = document.getElementById('hiddenDemoMode');

            if (previewName) previewName.textContent = data.candidateName || 'Rahim Ahmed (Senior .NET & AI Engineer)';
            if (previewSize) previewSize.textContent = 'Demo Preloaded Profile';
            if (previewCard) previewCard.classList.remove('d-none');
            if (dropzone) dropzone.classList.add('d-none');
            if (hiddenDemo) hiddenDemo.value = 'true';

        } catch (err) {
            console.error('Failed to load demo presets:', err);
        }
    });

    btnClear?.addEventListener('click', () => {
        document.querySelectorAll('.url-field').forEach(i => {
            i.value = '';
            i.dispatchEvent(new Event('input'));
        });
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

        if (urls.length === 0) {
            e.preventDefault();
            alert('Please enter at least one Bdjobs posting URL or Job ID.');
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
            { id: 'step1', pct: 20, text: 'Parsing resume text & chunking sections...' },
            { id: 'step2', pct: 45, text: 'Fetching Bdjobs postings from internal gateway...' },
            { id: 'step3', pct: 65, text: 'Generating vector embeddings with Ollama...' },
            { id: 'step4', pct: 85, text: 'In-memory cosine similarity qualification search...' },
            { id: 'step5', pct: 95, text: 'Structured LLM scoring & ranking results...' }
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

                // Update step indicators
                steps.forEach((step, idx) => {
                    const el = document.getElementById(step.id);
                    if (!el) return;
                    if (idx < currentStep) {
                        el.className = 'd-flex align-items-center gap-2 small progress-step step-completed';
                        el.querySelector('i').className = 'bi bi-check-circle-fill text-success';
                    } else if (idx === currentStep) {
                        el.className = 'd-flex align-items-center gap-2 small progress-step step-active';
                        el.querySelector('i').className = 'bi bi-arrow-repeat text-primary spin';
                    }
                });

                currentStep++;
            } else {
                clearInterval(stepInterval);
            }
        }, 2200);
    }
}
