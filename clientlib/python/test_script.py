

c = Client('test_user')

c.api_url = 'http://localhost:8080/api/'
game_response = c.create_game()

if c.update_game():
    print("Updated the game")


if c.join_game() :
    print("joined the game")


np = Client('test_user2')
np.api_url = c.api_url
np.game_id = c.game_id

np.join_game()


c.start_game()

np.update_player_info()
