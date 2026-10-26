// =====================================================================
// seed.js — тестові дані. ІДЕМПОТЕНТНИЙ (find-or-create):
// кожен документ має фіксований _id і вставляється через upsert + $setOnInsert,
// тому повторний запуск не створює дублікатів і не затирає змінені дані.
// movieId = Movies.Id у CinemaCatalog, author.customerId = Customers.Id у CinemaBooking.
// =====================================================================
db = db.getSiblingDB("cinema_reviews");

const OLENA = { customerId: 1, displayName: "Олена К." };
const TARAS = { customerId: 2, displayName: "Тарас М." };
const IRYNA = { customerId: 3, displayName: "Ірина Б." };
const d = s => new Date(s);

const D1 = ObjectId("670000000000000000000d01");
const D2 = ObjectId("670000000000000000000d02");

function seed(collection, docs) {
  const ops = docs.map(({ _id, ...rest }) => ({
    updateOne: { filter: { _id }, update: { $setOnInsert: rest }, upsert: true }
  }));
  const r = db.getCollection(collection).bulkWrite(ops);
  print(`${collection}: додано ${r.upsertedCount}, вже існувало ${docs.length - r.upsertedCount}`);
}

// ---------------- reviews: три види з різними наборами полів ----------------
seed("reviews", [
  {
    _id: ObjectId("670000000000000000000001"),
    movieId: 1, movieTitle: "Карпатський експрес",
    author: OLENA, kind: "full",
    rating: 5, title: "Найкращий фільм осені!",
    text: "Неймовірні краєвиди Карпат і закручений сюжет. Рекомендую всім.",
    hasSpoilers: false,
    photos: ["https://cdn.cinema-kadr.example/reviews/r1-1.jpg"],
    comments: [                                                   // embedded: кілька коментарів
      { author: "Тарас М.", text: "Згоден, фінал — вогонь!", createdAt: d("2026-10-02T19:10:00Z") },
      { author: "Ірина Б.", text: "Треба й собі сходити", createdAt: d("2026-10-03T08:45:00Z") }
    ],
    discussionId: D1,                                              // reference на обговорення
    likes: 14, createdAt: d("2026-10-02T18:30:00Z")
  },
  {
    _id: ObjectId("670000000000000000000002"),
    movieId: 1, movieTitle: "Карпатський експрес",
    author: TARAS, kind: "quick",                                  // лише оцінка — без тексту
    rating: 4, likes: 2, createdAt: d("2026-10-04T21:00:00Z")
  },
  {
    _id: ObjectId("670000000000000000000003"),
    movieId: 3, movieTitle: "Мандрівка до зірок",
    author: IRYNA, kind: "full",
    rating: 5, title: "Діти в захваті",
    text: "Сходили з донькою — вона досі будує ракети з коробок 🙂",
    hasSpoilers: false, comments: [], likes: 9, createdAt: d("2026-10-05T12:00:00Z")
  },
  {
    _id: ObjectId("670000000000000000000004"),
    movieId: 4, movieTitle: "Остання зміна",
    author: OLENA, kind: "critic",                                 // поля, яких немає в інших видах
    rating: 3, text: "Сильна акторська гра, але друга половина затягнута.",
    publication: "Блог «Кіно по-ужгородськи»",
    pros: ["акторська гра", "саундтрек"], cons: ["темп", "хронометраж"],
    hasSpoilers: true, likes: 5, createdAt: d("2026-10-06T10:15:00Z")
  },
  {
    _id: ObjectId("670000000000000000000005"),
    movieId: 2, movieTitle: "Таємниця старого маяка",
    author: TARAS, kind: "full",
    rating: 4, title: "Атмосферний детектив",
    text: "До останнього не здогадався, хто винен.",
    hasSpoilers: false,
    comments: [{ author: "Олена К.", text: "А я здогадалась на середині 😉", createdAt: d("2026-10-07T20:00:00Z") }],
    discussionId: D2,
    likes: 7, createdAt: d("2026-10-07T17:40:00Z")
  },
  {
    _id: ObjectId("670000000000000000000006"),
    movieId: 2, movieTitle: "Таємниця старого маяка",
    author: IRYNA, kind: "quick",
    rating: 2, likes: 0, createdAt: d("2026-10-08T22:10:00Z")
  }
]);

// ---------------- discussions: гібрид (останні повідомлення вбудовано) ----------------
seed("discussions", [
  {
    _id: D1,
    movieId: 1, movieTitle: "Карпатський експрес",
    title: "Де знімали сцену на віадуку?",
    author: IRYNA, tags: ["локації", "Карпати"], isPinned: true,
    stats: { messagesCount: 3, lastActivityAt: d("2026-10-05T09:30:00Z") },
    lastMessages: [
      { messageId: ObjectId("670000000000000000000a03"), author: "Тарас М.", excerpt: "Схоже на Ворохтянський віадук", createdAt: d("2026-10-05T09:30:00Z") }
    ],
    createdAt: d("2026-10-03T10:00:00Z")
  },
  {
    _id: D2,
    movieId: 2, movieTitle: "Таємниця старого маяка",
    title: "Теорії щодо фіналу (спойлери!)",
    author: TARAS, tags: ["спойлери"], isPinned: false,
    stats: { messagesCount: 1, lastActivityAt: d("2026-10-08T11:00:00Z") },
    lastMessages: [
      { messageId: ObjectId("670000000000000000000a04"), author: "Олена К.", excerpt: "Доглядач знав усе з самого початку", createdAt: d("2026-10-08T11:00:00Z") }
    ],
    createdAt: d("2026-10-08T10:00:00Z")
  }
]);

// ---------------- messages: окрема колекція (reference на discussions) ----------------
seed("messages", [
  { _id: ObjectId("670000000000000000000a01"), discussionId: D1, author: IRYNA, text: "Хто знає, де знімали сцену на віадуку?", replyTo: null, createdAt: d("2026-10-03T10:00:00Z") },
  { _id: ObjectId("670000000000000000000a02"), discussionId: D1, author: OLENA, text: "Здається, це під Ворохтою", replyTo: ObjectId("670000000000000000000a01"), createdAt: d("2026-10-04T15:20:00Z") },
  { _id: ObjectId("670000000000000000000a03"), discussionId: D1, author: TARAS, text: "Схоже на Ворохтянський віадук", replyTo: ObjectId("670000000000000000000a01"), createdAt: d("2026-10-05T09:30:00Z") },
  { _id: ObjectId("670000000000000000000a04"), discussionId: D2, author: OLENA, text: "Доглядач знав усе з самого початку", replyTo: null, createdAt: d("2026-10-08T11:00:00Z") }
]);

// ---------------- movieRatings: заповнюється агрегацією (04_queries.js, запит 1) ----------------
printjson({
  reviews: db.reviews.countDocuments(), discussions: db.discussions.countDocuments(),
  messages: db.messages.countDocuments()
});
