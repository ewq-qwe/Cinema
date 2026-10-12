// =====================================================================
// Скрипт 02: індекси — кожен під конкретний запит (createIndex ідемпотентний)
// =====================================================================
db = db.getSiblingDB("cinema_reviews");

// Запит: сторінка фільму — відгуки, нові спочатку
db.reviews.createIndex({ movieId: 1, createdAt: -1 }, { name: "ix_reviews_movie_recent" });
// Запит: «мої відгуки» глядача
db.reviews.createIndex({ "author.customerId": 1, createdAt: -1 }, { name: "ix_reviews_author" });
// Правило: один глядач — один відгук на фільм
db.reviews.createIndex({ movieId: 1, "author.customerId": 1 }, { name: "ux_reviews_movie_author", unique: true });

// Запит: теми обговорень фільму, закріплені й активні зверху
db.discussions.createIndex({ movieId: 1, isPinned: -1, "stats.lastActivityAt": -1 }, { name: "ix_discussions_movie" });

// Запит: повідомлення теми з пагінацією за часом
db.messages.createIndex({ discussionId: 1, createdAt: 1 }, { name: "ix_messages_discussion" });

// Запит: топ фільмів за рейтингом
db.movieRatings.createIndex({ average: -1, count: -1 }, { name: "ix_movieRatings_top" });

for (const c of ["reviews", "discussions", "messages", "movieRatings"])
  print(`${c}: ${db.getCollection(c).getIndexes().map(i => i.name).join(", ")}`);
