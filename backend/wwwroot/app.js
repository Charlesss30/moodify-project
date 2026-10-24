const API_BASE_URL = "/api";

// ==============================
// STATE
// ==============================
let currentUser = null;

// ==============================
// VIEW
// ==============================
const views = {
    login: document.getElementById("login-view"),
    register: document.getElementById("register-view"),
    home: document.getElementById("home-view")
};

function showView(name) {
    Object.entries(views).forEach(([key, element]) => {
        if (element) {
            element.classList.toggle("hidden", key !== name);
        }
    });

    if (name !== "home") {
        const modal = document.getElementById("logout-modal");

        if (modal) {
            modal.classList.add("hidden");
        }
    }

    if (window.lucide) {
        lucide.createIcons();
    }
}

// ==============================
// FIELD ERROR
// ==============================
function setFieldError(inputId, errorId, message) {
    const input = document.getElementById(inputId);
    const error = document.getElementById(errorId);

    if (!input || !error) return;

    const wrap = input.closest(".input-wrap");

    error.textContent = message || "";

    if (wrap) {
        wrap.classList.toggle("error", Boolean(message));
    }
}

function clearLoginErrors() {
    setFieldError(
        "login-identifier",
        "login-identifier-error",
        ""
    );

    setFieldError(
        "login-password",
        "login-password-error",
        ""
    );

    const generalError =
        document.getElementById("login-general-error");

    const success =
        document.getElementById("login-success");

    if (generalError) {
        generalError.classList.add("hidden");
    }

    if (success) {
        success.classList.add("hidden");
    }
}

function clearRegisterErrors() {
    setFieldError(
        "register-name",
        "register-name-error",
        ""
    );

    setFieldError(
        "register-email",
        "register-email-error",
        ""
    );

    setFieldError(
        "register-password",
        "register-password-error",
        ""
    );

    setFieldError(
        "register-confirm",
        "register-confirm-error",
        ""
    );
}

function emailValid(email) {
    return /^\S+@\S+\.\S+$/.test(email);
}

// ==============================
// REGISTER
// ==============================

const agreeTerms =
    document.getElementById("agree-terms");

const registerSubmit =
    document.getElementById("register-submit");

if (agreeTerms && registerSubmit) {

    agreeTerms.addEventListener("change", function () {

        registerSubmit.disabled = !this.checked;

    });
}


const registerForm =
    document.getElementById("register-form");

if (registerForm) {

    registerForm.addEventListener("submit", async function (e) {

        e.preventDefault();

        console.log("Registering...");


        const name =
            document.getElementById("register-name").value.trim();

        const email =
            document.getElementById("register-email").value.trim();

        const password =
            document.getElementById("register-password").value;

        const confirmPassword =
            document.getElementById("register-confirm").value;


        // ==============================
        // VALIDATE
        // ==============================

        if (!name) {
            alert("Please enter your name.");
            return;
        }

        if (!email) {
            alert("Please enter your email.");
            return;
        }

        if (!emailValid(email)) {
            alert("Invalid email address.");
            return;
        }

        if (!password || password.length < 6) {
            alert("Password must have at least 6 characters.");
            return;
        }

        if (password !== confirmPassword) {
            alert("Passwords do not match.");
            return;
        }

        if (!agreeTerms.checked) {
            alert("Please accept the terms.");
            return;
        }


        // ==============================
        // GỌI API
        // ==============================

        try {

            registerSubmit.disabled = true;
            registerSubmit.textContent = "Creating your account...";


            const response = await fetch(
                `${API_BASE_URL}/Auth/register`,
                {
                    method: "POST",

                    headers: {
                        "Content-Type": "application/json"
                    },

                    body: JSON.stringify({
                        tenDangNhap: name,
                        email: email,
                        matKhau: password
                    })
                }
            );


            const data = await response.json();

            console.log("Register API:", data);


            // ==============================
            // REGISTER FAIL
            // ==============================

            if (!response.ok) {

                alert(
                    data.message ||
                    "Registration failed."
                );

                return;
            }


            // ==============================
            // REGISTER SUCCESS
            // ==============================

            const successElement =
                document.getElementById("register-success");

            const successText =
                document.getElementById("register-success-text");


            if (successText) {

                successText.textContent =
                    "Registration successful! Redirecting to login...";
            }


            if (successElement) {

                successElement.classList.remove("hidden");
            }


            // Reset form

            registerForm.reset();

            registerSubmit.disabled = true;


            // Chuyển về Login sau 1.5 seconds

            setTimeout(() => {

                if (successElement) {
                    successElement.classList.add("hidden");
                }

                showView("login");

                // Điền sẵn email vừa đăng ký

                const loginIdentifier =
                    document.getElementById("login-identifier");

                if (loginIdentifier) {
                    loginIdentifier.value = email;
                }

            }, 1500);


        } catch (error) {

            console.error(
                "Register error:",
                error
            );

            alert(
                "Unable to connect to the API."
            );

        } finally {

            registerSubmit.disabled =
                !agreeTerms.checked;

            registerSubmit.textContent =
                "Create Account";
        }

    });
}

// ==============================
// LOGIN
// ==============================

const loginForm =
    document.getElementById("login-form");


if (loginForm) {

    loginForm.addEventListener("submit", async function (e) {

        e.preventDefault();

        console.log("Login button được bấm");


        const identifier =
            document
                .getElementById("login-identifier")
                .value
                .trim();

        const password =
            document
                .getElementById("login-password")
                .value;


        const errorElement =
            document.getElementById(
                "login-general-error"
            );

        const successElement =
            document.getElementById(
                "login-success"
            );


        // Delete thông báo cũ

        if (errorElement) {

            errorElement.textContent = "";

            errorElement.classList.add("hidden");
        }

        if (successElement) {

            successElement.textContent = "";

            successElement.classList.add("hidden");
        }


        // ==============================
        // VALIDATE
        // ==============================

        if (!identifier) {

            if (errorElement) {

                errorElement.textContent =
                    "Please enter your email or username.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        if (!password) {

            if (errorElement) {

                errorElement.textContent =
                    "Please enter your password.";

                errorElement.classList.remove("hidden");
            }

            return;
        }


        // ==============================
        // GỌI API LOGIN
        // ==============================

        try {

            console.log("Signing in...");


            const response = await fetch(
                `${API_BASE_URL}/Auth/login`,
                {
                    method: "POST",

                    headers: {
                        "Content-Type": "application/json"
                    },

                    body: JSON.stringify({
                        identifier: identifier,
                        matKhau: password
                    })
                }
            );


            const data = await response.json();

            console.log("Login API:", data);


            // ==============================
            // LOGIN FAIL
            // ==============================

            if (!response.ok) {

                if (errorElement) {

                    errorElement.textContent =
                        data.message ||
                        "Incorrect email or password.";

                    errorElement.classList.remove("hidden");
                }

                return;
            }


            // ==============================
            // LOGIN SUCCESS
            // ==============================

            console.log(
                "Login successful:",
                data.user
            );


            // Save user vào biến hiện tại

            currentUser = data.user;
            sessionStorage.setItem("moodify_user_token", data.token);


            // Save phiên đăng nhập

            localStorage.setItem(
                "moodify_user",
                JSON.stringify(currentUser)
            );

            if (["admin", "quantrivien", "aiengineer"].includes(String(currentUser.vaiTro).toLowerCase())) {
                const adminUrl = new URL(window.location.origin);
                adminUrl.port = '5174';
                window.location.replace(adminUrl.href);
                return;
            }


            // Nếu sau này dùng JWT
            // localStorage.setItem("access_token", data.token);


            // Hiển thị thông báo

            if (successElement) {

                successElement.textContent =
                    `Login successful! Hello ${currentUser.tenDangNhap}.`;

                successElement.classList.remove("hidden");
            }


            // ==============================
            // CẬP NHẬT HOME
            // ==============================

            updateHomeUser();


            // Chờ một chút để người dùng thấy thông báo

            setTimeout(() => {

                showView("home");
                window.dispatchEvent(new Event("moodify-user-login"));

            }, 700);


        } catch (error) {

            console.error(
                "Login error:",
                error
            );


            if (errorElement) {

                errorElement.textContent =
                    "Unable to connect to the server.";

                errorElement.classList.remove("hidden");
            }

        }

    });

}

// ==============================
// UPDATE HOME USER
// ==============================

function updateHomeUser() {

    if (!currentUser) {
        return;
    }


    const homeName =
        document.getElementById("home-name");

    const homeRole =
        document.getElementById("home-role");


    if (homeName) {

        homeName.textContent =
            currentUser.tenDangNhap;
    }


    if (homeRole) {

        homeRole.textContent =
            currentUser.vaiTro;
    }


    console.log(
        "Home user:",
        currentUser
    );
}

// ==============================
// PASSWORD SHOW / HIDE
// ==============================

function setupPasswordToggle(
    buttonId,
    inputId
) {

    const button =
        document.getElementById(buttonId);

    const input =
        document.getElementById(inputId);

    if (!button || !input) return;

    button.addEventListener(
        "click",
        function () {

            if (input.type === "password") {

                input.type = "text";

                button.innerHTML =
                    '<i data-lucide="eye-off"></i>';

            } else {

                input.type = "password";

                button.innerHTML =
                    '<i data-lucide="eye"></i>';
            }

            if (window.lucide) {

                lucide.createIcons();
            }
        }
    );
}

setupPasswordToggle(
    "login-toggle-password",
    "login-password"
);

setupPasswordToggle(
    "register-toggle-password",
    "register-password"
);

setupPasswordToggle(
    "register-toggle-confirm",
    "register-confirm"
);

// ==============================
// PAGE NAVIGATION
// ==============================

document
    .querySelectorAll("[data-nav]")
    .forEach(function (button) {

        button.addEventListener(
            "click",
            function () {

                showView(
                    button.dataset.nav
                );
            }
        );
    });

// ==============================
// BOTTOM NAVIGATION
// ==============================

document
    .querySelectorAll(".nav-item")
    .forEach(function (button) {

        button.addEventListener(
            "click",
            function () {

                document
                    .querySelectorAll(".nav-item")
                    .forEach(function (item) {

                        item.classList.remove(
                            "active"
                        );
                    });

                button.classList.add("active");

                const tab =
                    button.dataset.tab;

                const result =
                    document.getElementById(
                        "mood-result"
                    );

                if (!result) return;

                if (tab === "home") {

                    result.classList.add(
                        "hidden"
                    );

                    return;
                }

                result.textContent =
                    `${tab} is awaiting the backend connection.`;

                result.classList.remove(
                    "hidden"
                );
            }
        );
    });

// ==============================
// MOOD ANALYSIS
// ==============================

const analyzeButton =
    document.getElementById(
        "analyze-btn"
    );

const moodLanguage = document.getElementById("mood-language");
if (moodLanguage) {
    moodLanguage.addEventListener("change", () => {
        const input = document.getElementById("mood-input");
        const note = document.getElementById("mood-language-note");
        const isVietnamese = moodLanguage.value === "vi";
        if (input) input.placeholder = isVietnamese ? "Hãy mô tả cảm xúc của bạn bằng tiếng Việt..." : "Describe how you're feeling in English...";
        if (note) note.textContent = isVietnamese
            ? "PhoBERT hiển thị 3 nhãn cảm xúc hàng đầu; gợi ý dùng tọa độ VA tổng hợp từ phân bố lớp."
            : "RoBERTa estimates valence/arousal; the page also shows the three closest moods.";
    });
}

if (analyzeButton) {

    analyzeButton.addEventListener(
        "click",
        async function () {

            const input =
                document.getElementById(
                    "mood-input"
                );

            const result =
                document.getElementById(
                    "mood-result"
                );

            if (!input || !result) return;

            const text =
                input.value.trim();
            const language = moodLanguage?.value === "vi" ? "vi" : "en";

            if (!text) {

                result.textContent =
                    "Please describe your mood.";

                result.classList.remove(
                    "hidden"
                );

                return;
            }
            const token = sessionStorage.getItem("moodify_user_token");
            if (!token) {
                result.textContent = "Your session has expired. Please sign in again.";
                result.classList.remove("hidden");
                return;
            }

            analyzeButton.disabled = true;
            analyzeButton.textContent = "Analyzing…";
            result.textContent = language === "vi" ? "Đang phân tích bằng PhoBERT tiếng Việt…" : "Analyzing your mood with RoBERTa VA…";
            result.classList.remove("hidden");

            try {
                const response = await fetch(`${API_BASE_URL}/MoodAnalysis/valence-arousal`, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "Authorization": `Bearer ${token}`
                    },
                    body: JSON.stringify({ text, language })
                });
                const data = await response.json();
                if (!response.ok) throw new Error(data.message || "The VA model is unavailable.");

                result.replaceChildren();
                const summary = document.createElement("p");
                summary.textContent = `${language === "vi" ? "Mood ứng dụng gần nhất" : "Nearest app mood"}: ${data.mood.tenTamTrang} · Valence ${Number(data.valence).toFixed(2)} · Arousal ${Number(data.arousal).toFixed(2)}`;
                result.append(summary);

                const alternatives = language === "vi"
                    ? (data.emotionCandidates || []).map(item => `${item.label} (${Math.round(Number(item.score) * 100)}%)`)
                    : (data.moodCandidates || []).filter(item => Number(item.moodId ?? item.MoodId) !== Number(data.mood.tamTrangID))
                        .map(item => item.moodName ?? item.MoodName).filter(Boolean);
                if (alternatives.length) {
                    const candidateLine = document.createElement("p");
                    candidateLine.textContent = language === "vi"
                        ? `Các nhãn PhoBERT hàng đầu: ${alternatives.join(" · ")}`
                        : `Các mood gần tọa độ VA: ${alternatives.join(" · ")}`;
                    result.append(candidateLine);
                }

                if (language === "vi" && data.accepted === false) {
                    const uncertainty = document.createElement("p");
                    uncertainty.textContent = "Độ tin cậy dưới ngưỡng kiểm định; hãy diễn đạt rõ hơn rồi thử lại trước khi dùng gợi ý này.";
                    result.append(uncertainty);
                    return;
                }

                const recommend = document.createElement("button");
                recommend.type = "button";
                recommend.className = "small-primary-btn";
                recommend.textContent = language === "vi" ? "Gợi ý phim và nhạc theo điểm cảm xúc tổng hợp" : "Recommend movies and music from the VA score";
                recommend.addEventListener("click", async () => {
                    recommend.disabled = true;
                    recommend.textContent = language === "vi" ? "Đang tìm phim và nhạc…" : "Finding recommendations…";
                    try {
                        const recResponse = await fetch(`${API_BASE_URL}/Recommendation`, {
                            method: "POST",
                            headers: {
                                "Content-Type": "application/json",
                                "Authorization": `Bearer ${token}`
                            },
                            body: JSON.stringify({
                                mode: "mood",
                                source: "ai_va",
                                modelVersion: data.modelVersion,
                                tamTrangID: data.mood.tamTrangID,
                                valence: data.valence,
                                arousal: data.arousal,
                                limit: 12,
                                balanced: true
                            })
                        });
                        const recommendations = await recResponse.json();
                        if (!recResponse.ok) throw new Error(recommendations.message || "Could not load recommendations.");
                        const heading = document.createElement("h3");
                        heading.textContent = language === "vi" ? "Phim và nhạc phù hợp với cảm xúc" : "Movies and music for this mood";
                        const list = document.createElement("ul");
                        list.className = "va-recommendation-list";
                        for (const item of recommendations.items || []) {
                            const row = document.createElement("li");
                            const content = item.content || {};
                            const title = content.tieuDe || content.title || "Untitled";
                            const type = content.loaiNoiDung || content.contentType || "Content";
                            row.textContent = `${title} · ${type}`;
                            list.append(row);
                        }
                        if (!list.children.length) {
                            const empty = document.createElement("li");
                            empty.textContent = "No matching content is available yet.";
                            list.append(empty);
                        }
                        result.append(heading, list);
                        recommend.textContent = language === "vi" ? "Đã tải gợi ý" : "Recommendations loaded";
                    } catch (error) {
                        recommend.disabled = false;
                        recommend.textContent = error.message || "Retry recommendations";
                    }
                });
                result.append(recommend);
            } catch (error) {
                result.textContent = error.message || "Unable to analyze your mood.";
            } finally {
                analyzeButton.disabled = false;
                analyzeButton.textContent = "Analyze Mood";
            }
        }
    );
}

// ==============================
// LOGOUT
// ==============================

const logoutButton =
    document.getElementById(
        "logout-btn"
    );

const cancelLogout =
    document.getElementById(
        "cancel-logout"
    );

const confirmLogout =
    document.getElementById(
        "confirm-logout"
    );

if (logoutButton) {

    logoutButton.addEventListener(
        "click",
        function () {

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.remove(
                    "hidden"
                );
            }
        }
    );
}

if (cancelLogout) {

    cancelLogout.addEventListener(
        "click",
        function () {

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.add(
                    "hidden"
                );
            }
        }
    );
}

if (confirmLogout) {

    confirmLogout.addEventListener(
        "click",
        function () {

            currentUser = null;
            sessionStorage.removeItem("moodify_user_token");
            window.dispatchEvent(new Event("moodify-user-logout"));

            // Delete phiên đăng nhập
            localStorage.removeItem("moodify_user");

            // Sau này khi có JWT
            localStorage.removeItem("access_token");

            const modal =
                document.getElementById(
                    "logout-modal"
                );

            if (modal) {

                modal.classList.add(
                    "hidden"
                );
            }

            showView("login");
        }
    );
}

// ==============================
// INITIALIZE
// ==============================

showView("login");
