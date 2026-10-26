// =====================================================================
// queries.js — запити й агрегації
// =====================================================================
db = db.getSiblingDB("cinema_reviews");
const show = (title, value) => { print(`\n=== ${title} ===`); printjson(value); };

// 1. Агрегація: рейтинг кожного фільму -> read model movieRatings ($group + $merge, ідемпотентно)
db.reviews.aggregate([
  { $group: {
      _id: "$movieId",
      movieTitle: { $first: "$movieTitle" },
      average: { $avg: "$rating" },
      count: { $sum: 1 },
      r1: { $sum: { $cond: [{ $eq: ["$rating", 1] }, 1, 0] } },
      r2: { $sum: { $cond: [{ $eq: ["$rating", 2] }, 1, 0] } },
      r3: { $sum: { $cond: [{ $eq: ["$rating", 3] }, 1, 0] } },
      r4: { $sum: { $cond: [{ $eq: ["$rating", 4] }, 1, 0] } },
      r5: { $sum: { $cond: [{ $eq: ["$rating", 5] }, 1, 0] } }
  } },
  { $project: {
      movieTitle: 1, count: 1, average: { $round: ["$average", 2] },
      distribution: { "1": "$r1", "2": "$r2", "3": "$r3", "4": "$r4", "5": "$r5" },
      updatedAt: "$$NOW"
  } },
  { $merge: { into: "movieRatings", on: "_id", whenMatched: "replace", whenNotMatched: "insert" } }
]);
show("1. Топ фільмів (читання з read model)",
  db.movieRatings.find({}, { movieTitle: 1, average: 1, count: 1 }).sort({ average: -1, count: -1 }).toArray());

// 2. Гнучка схема: документи різної структури в одній колекції
show("2. Види відгуків і їхні поля", db.reviews.aggregate([
  { $project: { kind: 1, fields: { $map: { input: { $objectToArray: "$$ROOT" }, as: "f", in: "$$f.k" } } } },
  { $group: { _id: "$kind", fields: { $first: "$fields" }, n: { $sum: 1 } } }
]).toArray());

// 3. Пагінація: відгуки на фільм, нові спочатку (індекс ix_reviews_movie_recent)
const page = 1, pageSize = 2;
show("3. Відгуки на «Карпатський експрес», сторінка 1",
  db.reviews.find({ movieId: 1 }, { "author.displayName": 1, kind: 1, rating: 1, title: 1 })
    .sort({ createdAt: -1 }).skip((page - 1) * pageSize).limit(pageSize).toArray());

// 4. Embedding: відгук разом із вбудованими коментарями — одне читання
show("4. Відгук з коментарями", db.reviews.findOne(
  { _id: ObjectId("670000000000000000000001") }, { title: 1, rating: 1, comments: 1 }));

// 5. Referencing: відгук -> обговорення -> усі повідомлення ($lookup)
show("5. Відгук + обговорення + повідомлення", db.reviews.aggregate([
  { $match: { _id: ObjectId("670000000000000000000001") } },
  { $lookup: { from: "discussions", localField: "discussionId", foreignField: "_id", as: "discussion" } },
  { $unwind: "$discussion" },
  { $lookup: {
      from: "messages", localField: "discussion._id", foreignField: "discussionId", as: "messages",
      pipeline: [{ $sort: { createdAt: 1 } }, { $project: { _id: 0, "author.displayName": 1, text: 1 } }]
  } },
  { $project: { title: 1, "discussion.title": 1, messages: 1 } }
]).toArray());

// 6. Гібрид: стрічка тем — з вбудованими останніми повідомленнями, без звернень до messages
show("6. Обговорення фільму (останні повідомлення вбудовано)",
  db.discussions.find({ movieId: 1 }, { title: 1, "stats.messagesCount": 1, lastMessages: 1 })
    .sort({ isPinned: -1, "stats.lastActivityAt": -1 }).toArray());

// 7. Додати коментар у вбудований масив і лайк — атомарно (умова в фільтрі — щоб повторний запуск не дублював)
db.reviews.updateOne(
  { _id: ObjectId("670000000000000000000003"), "comments.text": { $ne: "Ми теж ходили!" } },
  { $push: { comments: { author: "Олена К.", text: "Ми теж ходили!", createdAt: new Date() } }, $inc: { likes: 1 } });

// 8. Перевірки цілісності
try {
  db.reviews.insertOne({ movieId: 5, movieTitle: "Місто без світла", author: { customerId: 2, displayName: "Тарас М." },
                         kind: "full", rating: 9, createdAt: new Date() });
} catch (e) { print(`\n8a. Валідатор відхилив rating = 9: ${e.message.split("\n")[0]}`); }

try {
  db.reviews.insertOne({ movieId: 1, movieTitle: "Карпатський експрес", author: { customerId: 1, displayName: "Олена К." },
                         kind: "quick", rating: 1, createdAt: new Date() });
} catch (e) { print(`8b. Другий відгук того ж глядача на той самий фільм: ${e.code === 11000 ? "E11000 duplicate key" : e.message}`); }

// 9. Подія з Каталогу «фільм перейменовано» — оновлюємо денормалізовану назву скрізь
// (тут — демонстрація без зміни даних: назва та сама)
const renamed = { movieId: 3, title: "Мандрівка до зірок" };
const r1 = db.reviews.updateMany({ movieId: renamed.movieId }, { $set: { movieTitle: renamed.title } });
const r2 = db.discussions.updateMany({ movieId: renamed.movieId }, { $set: { movieTitle: renamed.title } });
const r3 = db.movieRatings.updateOne({ _id: renamed.movieId }, { $set: { movieTitle: renamed.title } });
print(`\n9. Оновлення копій назви: reviews ${r1.matchedCount}, discussions ${r2.matchedCount}, movieRatings ${r3.matchedCount}`);
