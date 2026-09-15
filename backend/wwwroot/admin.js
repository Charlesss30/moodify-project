const API_BASE_URL = "/api";
const currentUser = JSON.parse(localStorage.getItem("moodify_user") || "null");

if (!currentUser || String(currentUser.vaiTro).toLowerCase() !== "admin") {
  window.location.replace("/");
} else {
  document.getElementById("admin-user").textContent = `${currentUser.tenDangNhap} · ${currentUser.vaiTro}`;

  const endpoints = {
    genres: { url: `${API_BASE_URL}/Genres`, count: "genre-count", list: "genres-list", error: "genres-error" },
    movies: { url: `${API_BASE_URL}/Movies`, count: "movie-count", list: "movies-list", error: "movies-error" },
    music: { url: `${API_BASE_URL}/Music`, count: "music-count", list: "music-list", error: "music-error" }
  };

  function getValue(item, ...keys) {
    const key = keys.find((candidate) => item[candidate] !== undefined);
    return key ? item[key] : "";
  }

  function renderItems(type, items) {
    const config = endpoints[type];
    document.getElementById(config.count).textContent = items.length;
    const list = document.getElementById(config.list);

    if (!items.length) {
      list.innerHTML = "<li>Chưa có dữ liệu.</li>";
      return;
    }

    list.innerHTML = items.slice(0, 8).map((item) => {
      if (type === "genres") {
        return `<li>${getValue(item, "tenTheLoai", "TenTheLoai")}</li>`;
      }

      const title = getValue(item, "tieuDe", "TieuDe") || "Không có tiêu đề";
      const id = getValue(item, "noiDungID", "NoiDungID");
      return `<li>${title}<span class="item-meta">${id}</span></li>`;
    }).join("");
  }

  async function loadItems(type) {
    const config = endpoints[type];
    const error = document.getElementById(config.error);
    error.textContent = "";

    try {
      const response = await fetch(config.url);
      const data = await response.json();
      if (!response.ok) throw new Error(data.message || "Không thể tải dữ liệu.");
      renderItems(type, data);
    } catch (loadError) {
      document.getElementById(config.count).textContent = "!";
      document.getElementById(config.list).innerHTML = "<li>Không tải được dữ liệu.</li>";
      error.textContent = loadError.message;
    }
  }

  Object.keys(endpoints).forEach((type) => {
    loadItems(type);
  });

  document.querySelectorAll("[data-refresh]").forEach((button) => {
    button.addEventListener("click", () => loadItems(button.dataset.refresh));
  });

  document.getElementById("logout-button").addEventListener("click", () => {
    localStorage.removeItem("moodify_user");
    localStorage.removeItem("access_token");
    window.location.replace("/");
  });
}
